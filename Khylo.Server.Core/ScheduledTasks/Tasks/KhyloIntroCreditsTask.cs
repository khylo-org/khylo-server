using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Controller.Dto;
using Khylo.Controller.Entities;
using Khylo.Controller.Entities.TV;
using Khylo.Controller.Library;
using Khylo.Controller.MediaSegments;
using Khylo.Data.Enums;
using Khylo.Model.Tasks;
using Khylo.Server.Implementations.MediaSegments;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Core.ScheduledTasks.Tasks;

/// <summary>
/// Finds intros and end credits across the library so players can offer Skip Intro, Skip Credits
/// and Next Episode. Each season is analysed together (episodes share their theme music); movies
/// one at a time. Already-analysed files are skipped, so after the first run only new media is
/// looked at.
/// </summary>
public class KhyloIntroCreditsTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IMediaSegmentManager _mediaSegmentManager;
    private readonly KhyloSegmentDetector _detector;
    private readonly ILogger<KhyloIntroCreditsTask> _logger;
    private readonly IUserManager _userManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="KhyloIntroCreditsTask"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="mediaSegmentManager">The media segment manager.</param>
    /// <param name="detector">The intro and credits detector.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="userManager">The user manager, to put shows people are watching first.</param>
    public KhyloIntroCreditsTask(ILibraryManager libraryManager, IMediaSegmentManager mediaSegmentManager, KhyloSegmentDetector detector, ILogger<KhyloIntroCreditsTask> logger, IUserManager userManager)
    {
        _userManager = userManager;
        _libraryManager = libraryManager;
        _mediaSegmentManager = mediaSegmentManager;
        _detector = detector;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "Detect intros and credits";

    /// <inheritdoc/>
    public string Description => "Listens for each show's theme music and looks for end credits, so players can skip intros and credits.";

    /// <inheritdoc/>
    public string Category => "Library";

    /// <inheritdoc/>
    public string Key => "KhyloDetectIntrosCredits";

    /// <inheritdoc/>
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        progress.Report(0);

        var episodes = Query(BaseItemKind.Episode).OfType<Episode>().ToList();
        var movies = Query(BaseItemKind.Movie).OfType<Video>().ToList();

        var seasons = episodes
            .Where(e => !e.SeasonId.Equals(Guid.Empty))
            .GroupBy(e => e.SeasonId)
            .Where(g => g.Any(e => _detector.GetResult(e.Id) is null))
            .Select(g => g.OrderBy(e => e.IndexNumber ?? int.MaxValue).Cast<Video>().ToList())
            .ToList();
        var newMovies = movies.Where(m => _detector.GetResult(m.Id) is null).ToList();

        // Items analysed before the credits check improved get just their credits looked at again
        var analysed = seasons.SelectMany(s => s).Select(e => e.Id).Concat(newMovies.Select(m => m.Id)).ToHashSet();
        // Shows and movies someone is part-way through go first
        var watching = WatchingNow();
        var recheck = episodes.Cast<Video>().Concat(movies)
            .Where(v => !analysed.Contains(v.Id) && _detector.NeedsCreditsCheck(v.Id))
            .OrderBy(v => watching.Contains(v is Episode e ? e.SeriesId : v.Id) ? 0 : 1)
            .ToList();

        var total = Math.Max(1, seasons.Sum(s => s.Count) + newMovies.Count + recheck.Count);
        var done = 0;

        foreach (var season in seasons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _detector.AnalyzeSeasonAsync(season, cancellationToken).ConfigureAwait(false);
                foreach (var episode in season)
                {
                    await StoreAsync(episode, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Couldn't analyse a season of {Series}", (season[0] as Episode)?.SeriesName);
            }

            done += season.Count;
            progress.Report(100.0 * done / total);
        }

        foreach (var movie in newMovies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _detector.AnalyzeMovieAsync(movie, cancellationToken).ConfigureAwait(false);
                await StoreAsync(movie, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Couldn't analyse {Movie}", movie.Name);
            }

            done++;
            progress.Report(100.0 * done / total);
        }

        foreach (var video in recheck)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await _detector.RecheckCreditsAsync(video, cancellationToken).ConfigureAwait(false))
                {
                    await StoreAsync(video, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Couldn't re-check the credits of {Name}", video.Name);
            }

            done++;
            progress.Report(100.0 * done / total);
        }

        progress.Report(100);
    }

    /// <inheritdoc/>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Never starts by itself: it reads whole movies over the network and is heavy on the CPU,
        // so it only runs when the owner starts it from Scheduled Tasks.
        return [];
    }

    private IReadOnlyList<BaseItem> Query(BaseItemKind kind) => _libraryManager.GetItemList(new InternalItemsQuery
    {
        IncludeItemTypes = [kind],
        IsVirtualItem = false,
        Recursive = true,
        DtoOptions = new DtoOptions(false),
        SourceTypes = [SourceType.Library],
    }).Where(i => i.IsFileProtocol && File.Exists(i.Path)).ToList();

    // Series and movies in anyone's Continue watching
    private HashSet<Guid> WatchingNow()
    {
        var ids = new HashSet<Guid>();
        foreach (var user in _userManager.GetUsers())
        {
            var resumable = _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = [BaseItemKind.Episode, BaseItemKind.Movie],
                IsResumable = true,
                Recursive = true,
                Limit = 100,
                DtoOptions = new DtoOptions(false),
            });
            foreach (var item in resumable)
            {
                ids.Add(item is Episode episode ? episode.SeriesId : item.Id);
            }
        }

        return ids;
    }

    // Refresh the item's stored segments so players see the new markers straight away
    private Task StoreAsync(BaseItem item, CancellationToken cancellationToken)
        => _mediaSegmentManager.RunSegmentPluginProviders(item, _libraryManager.GetLibraryOptions(item), false, cancellationToken);
}
