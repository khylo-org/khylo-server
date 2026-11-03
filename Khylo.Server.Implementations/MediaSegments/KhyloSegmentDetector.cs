using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Common.Configuration;
using Khylo.Controller.Entities;
using Khylo.Controller.MediaEncoding;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Implementations.MediaSegments;

/// <summary>
/// Finds intros and end credits by looking at the media itself, the way Plex does.
/// <para>
/// Intros: every episode of a season opens with the same theme music, so the audio of each
/// episode's opening is fingerprinted (Chromaprint) and compared with its neighbours; the longest
/// stretch they share is the intro. End credits for episodes are found the same way from the
/// closing minutes (shows reuse their end-credits music). Movies, and episodes whose endings don't
/// match, fall back to the look of credits: text on a black background running to the end.
/// </para>
/// Results are cached per item on disk, so each file is only analysed once.
/// </summary>
public partial class KhyloSegmentDetector
{
    /// <summary>Bump when the analysis changes so stored results are recomputed.</summary>
    private const int AnalysisVersion = 2;

    // Version of the end-credits check; results from an older one get their credits re-checked
    private const int CreditsCheckVersion = 2;

    // Studio and network logo cards that can follow the credits (seconds at the very end)
    private const double LogoTailSeconds = 30;

    // A lighter moment inside the credits (a logo, a flash) shorter than this doesn't end them
    private const double MaxCreditsGapSeconds = 6;

    // Credits found by music that are shorter than this are probably just a closing sting
    private const double ShortOutroSeconds = 45;

    /// <summary>Length of audio one Chromaprint point covers.</summary>
    private const double PointSeconds = 0.1238;

    /// <summary>Points whose bits differ by at most this much count as the same sound.</summary>
    private const int MaxBitErrors = 6;

    /// <summary>Short mismatches inside a shared stretch (about 3.5 seconds) don't break it.</summary>
    private const int MaxGapPoints = 28;

    private const double MinIntroSeconds = 15;
    private const double MaxIntroSeconds = 150;
    private const double MinOutroSeconds = 15;
    private const double MaxOutroSeconds = 480;
    private const double MinMovieCreditsSeconds = 45;

    /// <summary>Clips shorter than this (extras, trailers, shorts) have no intro or credits to skip.</summary>
    private const double MinEpisodeSeconds = 480;

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };

    private readonly ILogger<KhyloSegmentDetector> _logger;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly string _resultsPath;
    private readonly string _fingerprintPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="KhyloSegmentDetector"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="mediaEncoder">The media encoder (for the ffmpeg path).</param>
    /// <param name="applicationPaths">The application paths.</param>
    public KhyloSegmentDetector(ILogger<KhyloSegmentDetector> logger, IMediaEncoder mediaEncoder, IApplicationPaths applicationPaths)
    {
        _logger = logger;
        _mediaEncoder = mediaEncoder;
        _resultsPath = Path.Combine(applicationPaths.DataPath, "khylo-segments");
        _fingerprintPath = Path.Combine(_resultsPath, "fingerprints");
    }

    /// <summary>
    /// Gets the stored analysis for an item, if it has been analysed with the current version.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    /// <returns>The result, or null when the item still needs analysing.</returns>
    public KhyloSegmentResult? GetResult(Guid itemId)
    {
        var path = ResultPath(itemId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var result = JsonSerializer.Deserialize<KhyloSegmentResult>(File.ReadAllText(path), _jsonOptions);
            return result?.Version == AnalysisVersion ? result : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Analyses every episode of one season together.
    /// </summary>
    /// <param name="episodes">The season's episodes, in order.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task AnalyzeSeasonAsync(IReadOnlyList<Video> episodes, CancellationToken cancellationToken)
    {
        var playable = episodes.Where(e => Seconds(e) >= MinEpisodeSeconds && File.Exists(e.Path)).ToList();
        var intros = new Dictionary<Guid, uint[]>();
        var endings = new Dictionary<Guid, uint[]>();
        var endingStarts = new Dictionary<Guid, double>();

        foreach (var episode in playable)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var runtime = Seconds(episode);
            var introLength = Math.Min(runtime * 0.3, 600);
            var endingLength = Math.Min(runtime * 0.3, 420);
            intros[episode.Id] = await FingerprintAsync(episode, "intro", 0, introLength, cancellationToken).ConfigureAwait(false);
            endingStarts[episode.Id] = runtime - endingLength;
            endings[episode.Id] = await FingerprintAsync(episode, "ending", runtime - endingLength, endingLength, cancellationToken).ConfigureAwait(false);
        }

        for (var i = 0; i < playable.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var episode = playable[i];
            var runtime = Seconds(episode);

            // Compare with the nearest few episodes on either side
            var others = playable
                .Select((other, index) => (other, distance: Math.Abs(index - i)))
                .Where(o => o.distance > 0)
                .OrderBy(o => o.distance)
                .Take(4)
                .Select(o => o.other)
                .ToList();

            var result = new KhyloSegmentResult { Version = AnalysisVersion, CreditsVersion = CreditsCheckVersion };

            // Intro: the longest opening stretch shared with another episode
            var bestIntro = others
                .Select(o => SharedRun(intros[episode.Id], intros[o.Id]))
                .Where(r => r is not null)
                .Select(r => r!.Value)
                .Where(r => r.Seconds is >= MinIntroSeconds and <= MaxIntroSeconds)
                .OrderByDescending(r => r.Seconds)
                .FirstOrDefault();
            if (bestIntro.Seconds > 0)
            {
                result.IntroStart = Math.Round(bestIntro.Start, 2);
                result.IntroEnd = Math.Round(bestIntro.End, 2);
            }

            // End credits: shared closing music, which must run on towards the end of the episode
            var offset = endingStarts[episode.Id];
            var bestOutro = others
                .Select(o => SharedRun(endings[episode.Id], endings[o.Id]))
                .Where(r => r is not null)
                .Select(r => r!.Value)
                .Where(r => r.Seconds is >= MinOutroSeconds and <= MaxOutroSeconds && offset + r.End >= runtime - 90)
                .OrderByDescending(r => r.Seconds)
                .FirstOrDefault();
            if (bestOutro.Seconds > 0)
            {
                result.OutroStart = Math.Round(offset + bestOutro.Start, 2);
                // Credits that run (nearly) to the end skip to the end; otherwise keep any scene after them
                result.OutroEnd = offset + bestOutro.End >= runtime - 20 ? runtime : Math.Round(offset + bestOutro.End, 2);
            }

            await AddCreditsByLookAsync(episode, result, cancellationToken).ConfigureAwait(false);

            Save(episode.Id, result);
            _logger.LogInformation(
                "Khylo segments for {Name}: intro {IntroStart}-{IntroEnd}, credits {OutroStart}-{OutroEnd}",
                episode.Name,
                result.IntroStart,
                result.IntroEnd,
                result.OutroStart,
                result.OutroEnd);
        }

        // Short clips and unreadable files are marked done so they aren't retried every run
        foreach (var episode in episodes.Except(playable))
        {
            Save(episode.Id, new KhyloSegmentResult { Version = AnalysisVersion });
        }
    }

    /// <summary>
    /// Analyses a movie: its end credits, from how they look.
    /// </summary>
    /// <param name="movie">The movie.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task AnalyzeMovieAsync(Video movie, CancellationToken cancellationToken)
    {
        var result = new KhyloSegmentResult { Version = AnalysisVersion, CreditsVersion = CreditsCheckVersion };
        if (movie.RunTimeTicks is > 0 && File.Exists(movie.Path))
        {
            var runtime = Seconds(movie);
            var credits = await FindCreditsByLookAsync(movie, Math.Min(runtime * 0.2, 900), MinMovieCreditsSeconds, cancellationToken).ConfigureAwait(false);
            if (credits is not null)
            {
                result.OutroStart = credits.Value;
                result.OutroEnd = runtime;
            }
        }

        Save(movie.Id, result);
        _logger.LogInformation("Khylo segments for {Name}: credits {OutroStart}", movie.Name, result.OutroStart);
    }

    /// <summary>
    /// Gets a value indicating whether an analysed item's end credits should be checked again with
    /// the current credits check: episodes with no credits or only a short closing sting, and movies
    /// with no credits. Everything else just gets marked as checked.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <returns>Whether the item's stored result is from an older credits check.</returns>
    public bool NeedsCreditsCheck(Guid itemId) => GetResult(itemId) is { } result && result.CreditsVersion < CreditsCheckVersion;

    /// <summary>
    /// Checks an analysed item's end credits again with the current check, keeping its intro.
    /// </summary>
    /// <param name="video">The episode or movie.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Whether the item's credits changed.</returns>
    public async Task<bool> RecheckCreditsAsync(Video video, CancellationToken cancellationToken)
    {
        var result = GetResult(video.Id);
        if (result is null || result.CreditsVersion >= CreditsCheckVersion)
        {
            return false;
        }

        var before = (result.OutroStart, result.OutroEnd);
        var worthChecking = result.OutroStart is null || (result.OutroEnd - result.OutroStart) < ShortOutroSeconds;
        if (worthChecking && video.RunTimeTicks is > 0 && File.Exists(video.Path))
        {
            if (video is Khylo.Controller.Entities.TV.Episode)
            {
                await AddCreditsByLookAsync(video, result, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var runtime = Seconds(video);
                var credits = await FindCreditsByLookAsync(video, Math.Min(runtime * 0.2, 900), MinMovieCreditsSeconds, cancellationToken).ConfigureAwait(false);
                if (credits is not null)
                {
                    result.OutroStart = credits.Value;
                    result.OutroEnd = runtime;
                }
            }
        }

        result.CreditsVersion = CreditsCheckVersion;
        Save(video.Id, result);
        var changed = before != (result.OutroStart, result.OutroEnd);
        if (changed)
        {
            _logger.LogInformation("Khylo credits for {Name} re-checked: {OutroStart}-{OutroEnd}", video.Name, result.OutroStart, result.OutroEnd);
        }

        return changed;
    }

    /// <summary>
    /// Fills in or corrects an episode's end credits by how they look. Credits found by their music
    /// can be just a closing sting (the last seconds); when the screen has been showing credits for
    /// longer, the credits start where that began.
    /// </summary>
    private async Task AddCreditsByLookAsync(Video episode, KhyloSegmentResult result, CancellationToken cancellationToken)
    {
        var runtime = Seconds(episode);
        var byLook = await FindCreditsByLookAsync(episode, Math.Min(runtime * 0.2, 300), 20, cancellationToken).ConfigureAwait(false);
        if (byLook is null)
        {
            return;
        }

        if (result.OutroStart is null)
        {
            result.OutroStart = byLook.Value;
            result.OutroEnd = runtime;
        }
        else if ((result.OutroEnd - result.OutroStart) < ShortOutroSeconds && byLook.Value < result.OutroStart.Value && result.OutroStart.Value - byLook.Value <= MaxOutroSeconds)
        {
            // The music only caught the closing sting; the credits began where the screen went black
            result.OutroStart = byLook.Value;
        }
    }

    private static double Seconds(BaseItem item) => TimeSpan.FromTicks(item.RunTimeTicks ?? 0).TotalSeconds;

    /// <summary>
    /// The longest stretch two fingerprints share, as seconds into the first one.
    /// </summary>
    private static SharedStretch? SharedRun(uint[] a, uint[] b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return null;
        }

        // Candidate alignments come from points that match exactly; the most common ones win
        var positions = new Dictionary<uint, List<int>>();
        for (var j = 0; j < b.Length; j++)
        {
            if (!positions.TryGetValue(b[j], out var list))
            {
                list = new List<int>();
                positions[b[j]] = list;
            }

            list.Add(j);
        }

        var shiftVotes = new Dictionary<int, int>();
        for (var i = 0; i < a.Length; i++)
        {
            if (positions.TryGetValue(a[i], out var list) && list.Count < 20)
            {
                foreach (var j in list)
                {
                    shiftVotes[j - i] = shiftVotes.GetValueOrDefault(j - i) + 1;
                }
            }
        }

        SharedStretch? best = null;
        foreach (var shift in shiftVotes.OrderByDescending(v => v.Value).Take(25).Select(v => v.Key))
        {
            var from = Math.Max(0, -shift);
            var to = Math.Min(a.Length, b.Length - shift);
            int runStart = -1, lastMatch = -1;
            for (var i = from; i <= to; i++)
            {
                var match = i < to && BitOperations.PopCount(a[i] ^ b[i + shift]) <= MaxBitErrors;
                if (match && (runStart < 0 || i - lastMatch > MaxGapPoints))
                {
                    Consider(runStart, lastMatch);
                    runStart = i;
                }

                if (match)
                {
                    lastMatch = i;
                }
                else if (i == to)
                {
                    Consider(runStart, lastMatch);
                }
            }
        }

        return best;

        void Consider(int start, int end)
        {
            if (start < 0 || end <= start)
            {
                return;
            }

            var stretch = new SharedStretch(start * PointSeconds, (end + 1) * PointSeconds);
            if (best is null || stretch.Seconds > best.Value.Seconds)
            {
                best = stretch;
            }
        }
    }

    private string ResultPath(Guid itemId) => Path.Combine(_resultsPath, itemId.ToString("N", CultureInfo.InvariantCulture) + ".json");

    private void Save(Guid itemId, KhyloSegmentResult result)
    {
        Directory.CreateDirectory(_resultsPath);
        File.WriteAllText(ResultPath(itemId), JsonSerializer.Serialize(result, _jsonOptions));
    }

    /// <summary>
    /// The Chromaprint fingerprint of part of a file's first audio track (cached on disk).
    /// </summary>
    private async Task<uint[]> FingerprintAsync(Video video, string part, double start, double length, CancellationToken cancellationToken)
    {
        var cache = Path.Combine(_fingerprintPath, $"{video.Id:N}-{part}.bin");
        if (File.Exists(cache))
        {
            return ToPoints(await File.ReadAllBytesAsync(cache, cancellationToken).ConfigureAwait(false));
        }

        var args = string.Format(
            CultureInfo.InvariantCulture,
            "-hide_banner -loglevel error -threads 2 -ss {0:0.###} -t {1:0.###} -i \"{2}\" -map 0:a:0 -ac 2 -f chromaprint -fp_format raw -",
            Math.Max(0, start),
            length,
            video.Path);
        var (output, _) = await RunFfmpegAsync(args, captureStdout: true, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(_fingerprintPath);
        await File.WriteAllBytesAsync(cache, output, cancellationToken).ConfigureAwait(false);
        return ToPoints(output);
    }

    private static uint[] ToPoints(byte[] bytes)
    {
        var points = new uint[bytes.Length / 4];
        for (var i = 0; i < points.Length; i++)
        {
            points[i] = BitConverter.ToUInt32(bytes, i * 4);
        }

        return points;
    }

    /// <summary>
    /// Where end credits start, judged by sight: one frame a second over the end of the video, and
    /// from the credits' start to the end the screen stays mostly black (white text on black). Studio
    /// and network logo cards in the last few seconds, and brief lighter moments inside the credits,
    /// don't break that. Returns null when the ending doesn't look like credits.
    /// </summary>
    private async Task<double?> FindCreditsByLookAsync(Video video, double windowSeconds, double minSeconds, CancellationToken cancellationToken)
    {
        var runtime = Seconds(video);
        var windowStart = Math.Max(0, runtime - windowSeconds);
        var args = string.Format(
            CultureInfo.InvariantCulture,
            "-hide_banner -loglevel info -threads 2 -ss {0:0.###} -i \"{1}\" -an -sn -dn -vf \"fps=1,scale=160:-2,blackframe=amount=0:threshold=20\" -f null -",
            windowStart,
            video.Path);
        var (_, log) = await RunFfmpegAsync(args, captureStdout: false, cancellationToken).ConfigureAwait(false);

        var frames = BlackFrameRegex().Matches(log)
            .Select(m => (Time: windowStart + double.Parse(m.Groups["t"].Value, CultureInfo.InvariantCulture), Black: int.Parse(m.Groups["p"].Value, CultureInfo.InvariantCulture)))
            .OrderBy(f => f.Time)
            .ToList();
        if (frames.Count < 10 || frames[^1].Time < runtime - LogoTailSeconds)
        {
            return null;
        }

        // Credits sit on true black: at least half the picture is pure black (night scenes aren't)
        static bool IsDark((double Time, int Black) frame) => frame.Black >= 50;

        // Walk back from the end while the screen stays black. Logo cards in the last few seconds
        // don't count; a lighter stretch longer than a few seconds is the show itself, and the
        // credits start just after it.
        double? found = null;
        for (var k = frames.Count - 1; k >= 0; k--)
        {
            if (IsDark(frames[k]))
            {
                found = frames[k].Time;
            }
            else if (runtime - frames[k].Time <= LogoTailSeconds)
            {
                continue;
            }
            else if (found is null || found.Value - frames[k].Time > MaxCreditsGapSeconds)
            {
                break;
            }
        }

        if (found is not { } start)
        {
            return null;
        }

        if (runtime - start < minSeconds)
        {
            return null;
        }

        return Math.Round(start, 2);
    }

    private async Task<(byte[] Stdout, string Stderr)> RunFfmpegAsync(string arguments, bool captureStdout, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _mediaEncoder.EncoderPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        process.Start();
        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Lowering priority is a nicety; carry on without it
        }

        using var stdout = new MemoryStream();
        var copyOut = process.StandardOutput.BaseStream.CopyToAsync(captureStdout ? stdout : Stream.Null, cancellationToken);
        var readErr = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await Task.WhenAll(copyOut, readErr).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(true);
            }
            catch (InvalidOperationException)
            {
                // Already gone
            }

            throw;
        }

        return (stdout.ToArray(), await readErr.ConfigureAwait(false));
    }

    [GeneratedRegex(@"pblack:(?<p>\d+).*?\bt:(?<t>[\d.]+)", RegexOptions.CultureInvariant)]
    private static partial Regex BlackFrameRegex();

    private readonly record struct SharedStretch(double Start, double End)
    {
        public double Seconds => End - Start;
    }
}
