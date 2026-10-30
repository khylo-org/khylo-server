using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Controller.Entities;
using Khylo.Controller.Library;
using Khylo.Data.Enums;
using Khylo.Extensions;
using Khylo.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Core.Library.Validators;

/// <summary>
/// Shows a movie once when the same film sits in a library more than once (for example a
/// "Special Edition" folder next to a regular copy). After each scan, copies in the same library
/// that share a TMDb or IMDb id are merged into one title with a version for each copy, the same
/// as merging them by hand. The sharpest ordinary video file becomes the main version.
/// </summary>
public class MovieVersionsPostScanTask : ILibraryPostScanTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<MovieVersionsPostScanTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MovieVersionsPostScanTask" /> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="logger">The logger.</param>
    public MovieVersionsPostScanTask(
        ILibraryManager libraryManager,
        ILogger<MovieVersionsPostScanTask> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Merges duplicate copies of the same movie into versions.
    /// </summary>
    /// <param name="progress">The progress.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Task.</returns>
    public async Task Run(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var movies = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie],
            IsVirtualItem = false,
            Recursive = true,
        })
            .OfType<Video>()
            .Where(i => i.ExtraType is null && i.OwnerId.IsEmpty())
            .ToList();

        // Same library, same film: matched by TMDb id, or IMDb id when there is no TMDb id
        var groups = movies
            .Select(i => (Movie: i, Key: MatchKey(i)))
            .Where(i => i.Key is not null)
            .GroupBy(i => (Library: i.Movie.GetTopParent()?.Id ?? Guid.Empty, i.Key))
            .Select(g => g.Select(i => i.Movie).ToList())
            .Where(g => g.Count > 1 && !AlreadyMerged(g))
            .ToList();

        var merged = 0;
        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await MergeAsync(group).ConfigureAwait(false);
                merged++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not merge the copies of {MovieName}", group[0].Name);
            }

            progress.Report((double)merged / groups.Count * 100);
        }

        if (merged > 0)
        {
            _logger.LogInformation("Merged {Count} movies that were in a library more than once", merged);
        }

        progress.Report(100);
    }

    private static string? MatchKey(Video movie)
    {
        var tmdb = movie.GetProviderId(MetadataProvider.Tmdb);
        if (!string.IsNullOrEmpty(tmdb))
        {
            return "tmdb:" + tmdb;
        }

        var imdb = movie.GetProviderId(MetadataProvider.Imdb);
        return string.IsNullOrEmpty(imdb) ? null : "imdb:" + imdb;
    }

    // Every copy already points at the same main version, or is that main version
    private static bool AlreadyMerged(List<Video> group)
    {
        var primaries = group.Select(i => i.PrimaryVersionId ?? i.Id).Distinct().ToList();
        return primaries.Count == 1;
    }

    private async Task MergeAsync(List<Video> items)
    {
        // An existing main version stays the main version; otherwise the sharpest ordinary file
        var primaryVersion = items.FirstOrDefault(i => i.LinkedAlternateVersions.Length > 0 && !i.PrimaryVersionId.HasValue)
            ?? items
                .OrderBy(i => i.Video3DFormat.HasValue || i.VideoType != VideoType.VideoFile ? 1 : 0)
                .ThenByDescending(i => i.GetDefaultVideoStream()?.Width ?? 0)
                .ThenBy(i => i.Id)
                .First();

        var alternateVersions = primaryVersion.LinkedAlternateVersions.ToList();

        foreach (var item in items.Where(i => !i.Id.Equals(primaryVersion.Id)))
        {
            item.SetPrimaryVersionId(primaryVersion.Id);
            await _libraryManager.RerouteLinkedChildReferencesAsync(item.Id, primaryVersion.Id).ConfigureAwait(false);

            if (!alternateVersions.Any(i => i.ItemId.HasValue && i.ItemId.Value.Equals(item.Id)))
            {
                alternateVersions.Add(new LinkedChild
                {
                    ItemId = item.Id,
                    Type = LinkedChildType.LinkedAlternateVersion
                });
            }

            foreach (var linkedItem in item.LinkedAlternateVersions)
            {
                if (linkedItem.ItemId.HasValue && !alternateVersions.Any(i => i.ItemId.HasValue && i.ItemId.Value.Equals(linkedItem.ItemId.Value)))
                {
                    alternateVersions.Add(linkedItem);
                }
            }

            item.LinkedAlternateVersions = [];
            await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
        }

        primaryVersion.LinkedAlternateVersions = alternateVersions.ToArray();
        await primaryVersion.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
        _logger.LogInformation("Merged {Count} copies of {MovieName} into one title", items.Count, primaryVersion.Name);
    }
}
