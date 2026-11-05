using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Controller.Library;
using Khylo.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Core.Listening;

/// <summary>
/// Records what people listen to and watch, from the playback reports apps already send. A row is written when playback starts
/// and updated on every report, so nothing is lost when the server restarts mid-song.
/// </summary>
public sealed class ListeningRecorder : BackgroundService
{
    private static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(10);

    private readonly ISessionManager _sessions;
    private readonly ListeningStore _store;
    private readonly ILibraryManager _library;
    private readonly ILogger<ListeningRecorder> _logger;
    private readonly object _gate = new();
    private readonly Dictionary<string, Live> _live = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ListeningRecorder"/> class.
    /// </summary>
    /// <param name="sessions">The session manager, which reports playback.</param>
    /// <param name="store">The history.</param>
    /// <param name="library">The library manager.</param>
    /// <param name="logger">The logger.</param>
    public ListeningRecorder(ISessionManager sessions, ListeningStore store, ILibraryManager library, ILogger<ListeningRecorder> logger)
    {
        _sessions = sessions;
        _store = store;
        _library = library;
        _logger = logger;
    }

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _sessions.PlaybackStart += (_, e) => Report(e, false);
        _sessions.PlaybackProgress += (_, e) => Report(e, false);
        _sessions.PlaybackStopped += (_, e) => Report(e, true);
        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A crashed app or a phone that lost signal never says it stopped: forget sessions with no report for 10 minutes
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            lock (_gate)
            {
                foreach (var key in _live.Where(p => DateTime.UtcNow - p.Value.LastReport > IdleLimit).Select(p => p.Key).ToList())
                {
                    _live.Remove(key);
                }
            }
        }
    }

    private static string? Resolution(int? height) => height switch
    {
        null or <= 0 => null,
        >= 2000 => "4K",
        >= 1400 => "1440p",
        >= 1000 => "1080p",
        >= 700 => "720p",
        >= 460 => "480p",
        _ => "SD"
    };

    private void Report(PlaybackProgressEventArgs e, bool stopped)
    {
        try
        {
            if (e.IsAutomated || e.Item is null || e.Users.Count == 0)
            {
                return;
            }

            var userId = e.Users[0].Id;
            var key = $"{userId:N}|{e.DeviceId}|{e.Item.Id:N}";
            var ticks = e.PlaybackPositionTicks ?? 0;
            var now = DateTime.UtcNow;

            lock (_gate)
            {
                if (!_store.IsRecordingEnabled(userId))
                {
                    _live.Remove(key);
                    return;
                }

                if (!_live.TryGetValue(key, out var live))
                {
                    var play = ListeningSnapshot.Describe(e.Item, _library);
                    if (play is null || stopped)
                    {
                        return;
                    }

                    FillPlayer(play, e, userId, now);
                    live = new Live(_store.Insert(play, out _), play) { LastTicks = ticks, LastReport = now, Paused = e.IsPaused };
                    _live[key] = live;
                    return;
                }

                live.Seconds += ListeningKeys.ListenedSince(live.LastTicks, ticks, (now - live.LastReport).TotalSeconds, live.Paused);
                live.LastTicks = ticks;
                live.LastReport = now;
                live.Paused = e.IsPaused;

                var video = live.Play.Kind is "movie" or "episode";
                var counted = ListeningKeys.IsCounted(video, live.Seconds, live.Play.DurationSeconds);
                long? bytes = live.Play.Bitrate is { } bitrate ? (long)(bitrate / 8.0 * live.Seconds) : null;
                var method = e.Session?.PlayState?.PlayMethod?.ToString();
                var reasons = TranscodeReasonsOf(e);
                if (video && method is not null)
                {
                    live.Play.PlayMethod = method;
                }

                _store.Update(live.RowId, live.Seconds, counted, bytes, video ? method : null, video ? reasons : null);

                if (stopped)
                {
                    _live.Remove(key);
                }
            }
        }
        catch (Exception ex)
        {
            // Recording must never get in the way of playback; say what failed, never what was played
            _logger.LogWarning(ex, "Could not record a play");
        }
    }

    private static string? TranscodeReasonsOf(PlaybackProgressEventArgs e)
    {
        var reasons = e.Session?.TranscodingInfo?.TranscodeReasons;
        return reasons is null || reasons.Value == 0 ? null : reasons.Value.ToString();
    }

    private void FillPlayer(ListeningPlay play, PlaybackProgressEventArgs e, Guid userId, DateTime now)
    {
        play.UserId = userId;
        play.StartedAt = now;
        play.Client = e.ClientName;
        play.DeviceName = e.DeviceName;
        play.IsLocal = ListeningNetwork.IsLocal(e.Session?.RemoteEndPoint);
        if (play.Kind is not ("movie" or "episode"))
        {
            return;
        }

        play.PlayMethod = e.Session?.PlayState?.PlayMethod?.ToString();
        play.TranscodeReasons = TranscodeReasonsOf(e);
        play.Resolution = Resolution(e.MediaInfo?.Height);
        play.VideoCodec = e.MediaInfo?.MediaStreams?.FirstOrDefault(s => s.Type.ToString() == "Video")?.Codec;
        play.Bitrate = e.MediaInfo?.MediaSources?.FirstOrDefault()?.Bitrate;
    }

    private sealed class Live
    {
        public Live(long rowId, ListeningPlay play)
        {
            RowId = rowId;
            Play = play;
        }

        public long RowId { get; }

        public ListeningPlay Play { get; }

        public long LastTicks { get; set; }

        public DateTime LastReport { get; set; }

        public bool Paused { get; set; }

        public double Seconds { get; set; }
    }
}
