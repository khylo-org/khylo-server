using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Controller.Chapters;
using Khylo.Controller.Configuration;
using Khylo.Controller.Entities;
using Khylo.Controller.Library;
using Khylo.Controller.MediaSegments;
using Khylo.Database.Implementations.Enums;
using Khylo.Model;
using Khylo.Model.Entities;
using Khylo.Model.MediaSegments;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Implementations.MediaSegments;

/// <summary>
/// Khylo unified intro and credit detection segment provider.
/// Inspects embedded chapter markers and structural heuristics to generate client-queryable skip markers.
/// </summary>
public partial class KhyloMediaSegmentProvider : IMediaSegmentProvider
{
    private static readonly long MaxIntroStart = TimeSpan.FromMinutes(15).Ticks;
    private static readonly long MinIntroLength = TimeSpan.FromSeconds(5).Ticks;
    private static readonly long MaxIntroLength = TimeSpan.FromMinutes(3).Ticks;
    private static readonly long MinCreditsLength = TimeSpan.FromSeconds(10).Ticks;
    private static readonly long MaxRecapLength = TimeSpan.FromMinutes(5).Ticks;

    private readonly ILogger<KhyloMediaSegmentProvider> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly IChapterManager _chapterManager;
    private readonly IServerConfigurationManager _serverConfigurationManager;
    private readonly KhyloSegmentDetector _detector;

    /// <summary>
    /// Initializes a new instance of the <see cref="KhyloMediaSegmentProvider"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="chapterManager">Chapter manager.</param>
    /// <param name="serverConfigurationManager">Server configuration manager.</param>
    /// <param name="detector">Finds intros and credits from the media itself.</param>
    public KhyloMediaSegmentProvider(
        ILogger<KhyloMediaSegmentProvider> logger,
        ILibraryManager libraryManager,
        IChapterManager chapterManager,
        IServerConfigurationManager serverConfigurationManager,
        KhyloSegmentDetector detector)
    {
        _detector = detector;
        _logger = logger;
        _libraryManager = libraryManager;
        _chapterManager = chapterManager;
        _serverConfigurationManager = serverConfigurationManager;
    }

    /// <inheritdoc/>
    public string Name => "Khylo Intro & Credit Marker Engine";

    /// <inheritdoc/>
    public ValueTask<bool> Supports(BaseItem item)
    {
        return ValueTask.FromResult(item is Video);
    }

    /// <inheritdoc/>
    public Task CleanupExtractedData(Guid itemId, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Segments are only produced from explicitly named chapter markers (e.g. "Intro", "Opening Credits",
    /// "End Credits", "Previously On"). Nothing is guessed from chapter positions: a wrong segment makes
    /// "Skip Intro" jump into the episode, which is worse than showing no button at all.
    /// Each named chapter must also sit where that kind of segment plausibly lives and have a sane length.
    /// </remarks>
    public Task<IReadOnlyList<MediaSegmentDto>> GetMediaSegments(MediaSegmentGenerationRequest request, CancellationToken cancellationToken)
    {
        var item = _libraryManager.GetItemById(request.ItemId);
        if (item is not Video video || video.RunTimeTicks is not > 0)
        {
            return Task.FromResult<IReadOnlyList<MediaSegmentDto>>(Array.Empty<MediaSegmentDto>());
        }

        var runtime = video.RunTimeTicks.Value;
        var config = _serverConfigurationManager.Configuration;
        var segments = new List<MediaSegmentDto>();
        var chapters = _chapterManager.GetChapters(video.Id);

        for (var i = 0; i < chapters.Count; i++)
        {
            var name = chapters[i].Name?.Trim() ?? string.Empty;

            // "Intro end" / "Credits end" style names mark where a segment stops, not a segment themselves.
            if (string.IsNullOrEmpty(name) || EndMarkerRegex().IsMatch(name))
            {
                continue;
            }

            var startTicks = chapters[i].StartPositionTicks;
            var endTicks = i < chapters.Count - 1 ? chapters[i + 1].StartPositionTicks : runtime;
            var length = endTicks - startTicks;
            if (startTicks < 0 || length <= 0 || endTicks > runtime)
            {
                continue;
            }

            MediaSegmentType? type = null;
            if (config.EnableIntroDetection
                && IntroRegex().IsMatch(name)
                && startTicks <= Math.Min(runtime / 4, MaxIntroStart)
                && length >= MinIntroLength
                && length <= MaxIntroLength)
            {
                type = MediaSegmentType.Intro;
            }
            else if (config.EnableCreditsDetection
                && CreditRegex().IsMatch(name)
                && startTicks >= runtime / 2
                && length >= MinCreditsLength)
            {
                type = MediaSegmentType.Outro;
            }
            else if (RecapRegex().IsMatch(name)
                && startTicks <= runtime / 4
                && length <= MaxRecapLength)
            {
                type = MediaSegmentType.Recap;
            }
            else if (PreviewRegex().IsMatch(name)
                && startTicks >= runtime / 2)
            {
                type = MediaSegmentType.Preview;
            }

            if (type is null)
            {
                continue;
            }

            segments.Add(new MediaSegmentDto
            {
                Id = Guid.NewGuid(),
                ItemId = video.Id,
                Type = type.Value,
                StartTicks = startTicks,
                EndTicks = endTicks
            });
        }

        // Named chapters win; otherwise use what the detector found in the audio and picture
        var detected = _detector.GetResult(video.Id);
        if (detected is not null)
        {
            if (config.EnableIntroDetection
                && detected.IntroStart is { } introStart
                && detected.IntroEnd is { } introEnd
                && !segments.Exists(s => s.Type == MediaSegmentType.Intro))
            {
                segments.Add(new MediaSegmentDto
                {
                    Id = Guid.NewGuid(),
                    ItemId = video.Id,
                    Type = MediaSegmentType.Intro,
                    StartTicks = TimeSpan.FromSeconds(introStart).Ticks,
                    EndTicks = TimeSpan.FromSeconds(introEnd).Ticks
                });
            }

            if (config.EnableCreditsDetection
                && detected.OutroStart is { } outroStart
                && detected.OutroEnd is { } outroEnd
                && !segments.Exists(s => s.Type == MediaSegmentType.Outro))
            {
                segments.Add(new MediaSegmentDto
                {
                    Id = Guid.NewGuid(),
                    ItemId = video.Id,
                    Type = MediaSegmentType.Outro,
                    StartTicks = TimeSpan.FromSeconds(outroStart).Ticks,
                    EndTicks = Math.Min(TimeSpan.FromSeconds(outroEnd).Ticks, runtime)
                });
            }
        }

        _logger.LogDebug("Khylo chapter segment provider extracted {Count} segments for {ItemName} ({ItemId})", segments.Count, video.Name, video.Id);
        return Task.FromResult<IReadOnlyList<MediaSegmentDto>>(segments);
    }

    // Whole-name "OP"/"ED" are common anime chapter names; anywhere else those letters are too ambiguous.
    [GeneratedRegex(@"^(op|op\s*\d*)$|\b(intro|introduction|opening|opening\s*(credits|titles|theme)|theme\s*song|main\s*titles?|title\s*sequence|cold\s*open)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IntroRegex();

    [GeneratedRegex(@"^(ed|ed\s*\d*)$|\b(credits?|end\s*credits?|closing\s*credits?|end\s*titles|ending|outro)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CreditRegex();

    [GeneratedRegex(@"\b(recap|previously(\s*on)?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RecapRegex();

    [GeneratedRegex(@"\b(preview|next\s*time|next\s*episode|next\s*week)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PreviewRegex();

    [GeneratedRegex(@"\b(end|stop|finish)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EndMarkerRegex();
}
