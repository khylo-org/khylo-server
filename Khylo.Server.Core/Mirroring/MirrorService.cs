using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Controller.Mirroring;
using Khylo.Controller.Session;
using Khylo.Model.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Core.Mirroring;

/// <summary>
/// Keeps a person's devices playing the same thing. Once a second it compares the followed device with each follower and sends the
/// follower whatever it is missing: the item, a pause or resume, a stop, or a seek when it has drifted too far.
/// </summary>
public sealed class MirrorService : BackgroundService, IMirrorService
{
    // What a follower's app looks for at the start of a string command that means "tune to this Khylo TV channel"
    private const string ChannelCommandPrefix = "khylo-tv-channel:";

    // How far a follower may be from the followed device before the server moves it. Apps that correct themselves do it far sooner
    private static readonly TimeSpan DriftLimit = TimeSpan.FromMilliseconds(800);

    // A follower is left alone this long after it was told to play (it is still loading)
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(8);

    // Between two corrections of the same follower
    private static readonly TimeSpan CorrectionGap = TimeSpan.FromSeconds(10);

    // A follower that asked for the reference this recently is correcting itself
    private static readonly TimeSpan SelfCorrectingFor = TimeSpan.FromSeconds(5);

    // Progress reports lag a little behind, so a correction aims slightly ahead
    private static readonly TimeSpan Latency = TimeSpan.FromMilliseconds(400);

    // Between two items an app reports nothing playing for a moment; only a longer gap counts as stopped
    private static readonly TimeSpan StopDelay = TimeSpan.FromSeconds(5);

    // A device watching a Khylo TV channel says so every few seconds; a report older than this means it has left
    private static readonly TimeSpan ChannelFreshFor = TimeSpan.FromSeconds(45);

    // A followed device that has been gone this long ends its mirror
    private static readonly TimeSpan GoneAfter = TimeSpan.FromMinutes(2);

    private readonly ISessionManager _sessions;
    private readonly ILogger<MirrorService> _logger;
    private readonly object _gate = new();
    private readonly Dictionary<string, Mirror> _mirrors = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ChannelReport> _channels = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="MirrorService"/> class.
    /// </summary>
    /// <param name="sessions">The session manager.</param>
    /// <param name="logger">The logger.</param>
    public MirrorService(ISessionManager sessions, ILogger<MirrorService> logger)
    {
        _sessions = sessions;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<MirrorInfo> List(Guid userId)
    {
        lock (_gate)
        {
            return _mirrors.Values.Where(m => m.UserId.Equals(userId)).Select(Describe).ToList();
        }
    }

    /// <inheritdoc />
    public MirrorInfo Start(Guid userId, string sourceSessionId, IReadOnlyCollection<string> targetSessionIds)
    {
        var sessions = _sessions.Sessions.ToList();
        var source = sessions.FirstOrDefault(s => string.Equals(s.Id, sourceSessionId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("That device isn't online any more.");
        if (!source.UserId.Equals(userId))
        {
            throw new InvalidOperationException("That isn't your device.");
        }

        var targets = new List<SessionInfo>();
        foreach (var id in targetSessionIds.Distinct(StringComparer.Ordinal).Where(i => !string.Equals(i, source.Id, StringComparison.Ordinal)))
        {
            var target = sessions.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal))
                ?? throw new InvalidOperationException("A device isn't online any more.");
            if (!target.UserId.Equals(userId))
            {
                throw new InvalidOperationException("That isn't your device.");
            }

            if (!target.IsActive || !target.SupportsRemoteControl)
            {
                throw new InvalidOperationException($"{target.DeviceName} can't be controlled. Open Khylo on it, signed in as you.");
            }

            targets.Add(target);
        }

        if (targets.Count == 0)
        {
            throw new InvalidOperationException("Pick at least one other device.");
        }

        lock (_gate)
        {
            // A device is in one mirror at a time, and a device that follows can't also be followed
            foreach (var target in targets)
            {
                RemoveTarget(target.Id);
                _mirrors.Remove(target.Id);
            }

            if (!_mirrors.TryGetValue(source.Id, out var mirror))
            {
                RemoveTarget(source.Id);
                mirror = new Mirror { UserId = userId, SourceId = source.Id };
                _mirrors[source.Id] = mirror;
            }

            foreach (var target in targets)
            {
                mirror.Targets[target.Id] = new Follower { NeedsPlay = true };
            }

            // If the followed device is playing now, the new followers join in on the next beat
            _logger.LogInformation("Mirroring {Source} to {Count} device(s)", source.DeviceName, mirror.Targets.Count);
            return Describe(mirror);
        }
    }

    /// <inheritdoc />
    public MirrorReference? ReferenceFor(Guid userId, string deviceId)
    {
        var sessions = _sessions.Sessions.ToList();
        var mine = sessions.FirstOrDefault(s => s.UserId.Equals(userId) && string.Equals(s.DeviceId, deviceId, StringComparison.Ordinal));
        if (mine is null)
        {
            return null;
        }

        Mirror? mirror;
        Follower? follower = null;
        lock (_gate)
        {
            mirror = _mirrors.Values.FirstOrDefault(m => m.UserId.Equals(userId) && m.Targets.TryGetValue(mine.Id, out follower));
        }

        var source = mirror is null ? null : sessions.FirstOrDefault(s => string.Equals(s.Id, mirror.SourceId, StringComparison.Ordinal));
        if (follower is null || source?.NowPlayingItem is not { } item || PositionNow(source, DateTime.UtcNow) is not { } position)
        {
            return null;
        }

        Volatile.Write(ref follower.AskedAtTicks, DateTime.UtcNow.Ticks);
        return new MirrorReference { ItemId = item.Id, PositionTicks = position, IsPaused = source.PlayState?.IsPaused ?? false };
    }

    /// <inheritdoc />
    public void SetChannel(Guid userId, string deviceId, string? channelId)
    {
        var mine = _sessions.Sessions.FirstOrDefault(s => s.UserId.Equals(userId) && string.Equals(s.DeviceId, deviceId, StringComparison.Ordinal));
        if (mine is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(channelId))
        {
            _channels.TryRemove(mine.Id, out _);
        }
        else
        {
            _channels[mine.Id] = new ChannelReport(channelId, DateTime.UtcNow);
        }
    }

    /// <inheritdoc />
    public bool Stop(Guid userId, string sessionId)
    {
        lock (_gate)
        {
            if (_mirrors.TryGetValue(sessionId, out var mirror) && mirror.UserId.Equals(userId))
            {
                return _mirrors.Remove(sessionId);
            }

            return _mirrors.Values.Where(m => m.UserId.Equals(userId)).ToList().Aggregate(false, (found, m) => RemoveTarget(sessionId, m) || found);
        }
    }

    /// <summary>
    /// Looks at every mirror once and sends whatever its followers need.
    /// </summary>
    /// <param name="now">The time to judge by.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when every mirror has been looked at.</returns>
    public async Task TickAsync(DateTime now, CancellationToken cancellationToken)
    {
        List<Mirror> mirrors;
        lock (_gate)
        {
            mirrors = _mirrors.Values.ToList();
        }

        var sessions = _sessions.Sessions.ToDictionary(s => s.Id, StringComparer.Ordinal);
        foreach (var mirror in mirrors)
        {
            try
            {
                await FollowAsync(mirror, sessions, now, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One bad device must not stop the others from being followed
                _logger.LogWarning("Couldn't update a mirror: {Reason}", ex.Message);
            }
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
                await TickAsync(DateTime.UtcNow, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    // Where a device is right now, from its last report and how long ago that was
    private static long? PositionNow(SessionInfo session, DateTime now)
    {
        var state = session.PlayState;
        if (state?.PositionTicks is not { } ticks)
        {
            return null;
        }

        if (!state.IsPaused && session.LastPlaybackCheckIn > DateTime.MinValue)
        {
            var behind = now - session.LastPlaybackCheckIn;
            if (behind > TimeSpan.Zero && behind < TimeSpan.FromSeconds(30))
            {
                ticks += behind.Ticks;
            }
        }

        return ticks;
    }

    private static bool IsItem(Guid? playing, Guid wanted) => playing is { } id && id.Equals(wanted);

    private static bool CanDrift(SessionInfo source) =>
        source.PlayState is { CanSeek: true, IsPaused: false } && source.NowPlayingItem?.RunTimeTicks is > 0;

    private MirrorInfo Describe(Mirror mirror)
    {
        var sessions = _sessions.Sessions.ToDictionary(s => s.Id, StringComparer.Ordinal);
        sessions.TryGetValue(mirror.SourceId, out var source);
        return new MirrorInfo
        {
            SourceSessionId = mirror.SourceId,
            SourceName = source?.DeviceName,
            NowPlayingName = source?.NowPlayingItem?.Name,
            Targets = mirror.Targets.Keys
                .Select(id => sessions.TryGetValue(id, out var t)
                    ? new MirrorTargetInfo { SessionId = id, DeviceName = t.DeviceName, Client = t.Client }
                    : new MirrorTargetInfo { SessionId = id })
                .ToList()
        };
    }

    private bool RemoveTarget(string sessionId)
    {
        var removed = false;
        foreach (var mirror in _mirrors.Values.ToList())
        {
            removed |= RemoveTarget(sessionId, mirror);
        }

        return removed;
    }

    private bool RemoveTarget(string sessionId, Mirror mirror)
    {
        if (!mirror.Targets.Remove(sessionId))
        {
            return false;
        }

        if (mirror.Targets.Count == 0)
        {
            _mirrors.Remove(mirror.SourceId);
        }

        return true;
    }

    private async Task FollowAsync(Mirror mirror, Dictionary<string, SessionInfo> sessions, DateTime now, CancellationToken cancellationToken)
    {
        if (!sessions.TryGetValue(mirror.SourceId, out var source) || !source.IsActive)
        {
            mirror.MissingSince ??= now;
            if (now - mirror.MissingSince > GoneAfter)
            {
                lock (_gate)
                {
                    _mirrors.Remove(mirror.SourceId);
                }
            }

            return;
        }

        mirror.MissingSince = null;
        List<(string Id, Follower State)> members;
        lock (_gate)
        {
            members = mirror.Targets.Select(p => (p.Key, p.Value)).ToList();
        }

        var followers = members
            .Where(m => sessions.TryGetValue(m.Id, out var s) && s.IsActive)
            .Select(m => (Session: sessions[m.Id], m.State))
            .ToList();
        var item = source.NowPlayingItem;
        if (item is null && _channels.TryGetValue(source.Id, out var report) && now - report.At < ChannelFreshFor)
        {
            await FollowChannelAsync(mirror, source, followers, report.ChannelId, now, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (item is null)
        {
            // Nothing playing on the followed device: wait out the gap between items, then let the followers go quiet
            mirror.NothingSince ??= now;
            if (mirror.LastItem is not null && now - mirror.NothingSince > StopDelay)
            {
                foreach (var (session, _) in followers.Where(f => f.Session.NowPlayingItem is not null))
                {
                    await Command(source, session, PlaystateCommand.Stop, null, cancellationToken).ConfigureAwait(false);
                }

                mirror.LastItem = null;
            }

            return;
        }

        mirror.NothingSince = null;
        mirror.LastChannel = null;
        var paused = source.PlayState?.IsPaused ?? false;
        var sourcePosition = PositionNow(source, now);

        // A new item: every follower needs it
        if (!IsItem(mirror.LastItem, item.Id))
        {
            mirror.LastItem = item.Id;
            mirror.LastPaused = paused;
            foreach (var (_, state) in followers)
            {
                state.NeedsPlay = true;
            }
        }

        // New followers, and everyone when the item changed: play it from where the followed device is (live TV has no position: a follower joins what is on now)
        var started = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (session, state) in followers.Where(f => f.State.NeedsPlay))
        {
            await Play(source, session, item.Id, item.RunTimeTicks is > 0 ? sourcePosition : null, cancellationToken).ConfigureAwait(false);
            state.NeedsPlay = false;
            state.PlayedAt = now;
            started.Add(session.Id);
            if (paused)
            {
                await Command(source, session, PlaystateCommand.Pause, null, cancellationToken).ConfigureAwait(false);
            }
        }

        if (paused != mirror.LastPaused)
        {
            mirror.LastPaused = paused;
            foreach (var (session, _) in followers.Where(f => !started.Contains(f.Session.Id)))
            {
                await Command(source, session, paused ? PlaystateCommand.Pause : PlaystateCommand.Unpause, null, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        foreach (var (session, state) in followers.Where(f => !started.Contains(f.Session.Id)))
        {
            if (now - state.PlayedAt < SettleTime)
            {
                continue;
            }

            if (!IsItem(session.NowPlayingItem?.Id, item.Id))
            {
                // The person stopped it or picked something else on that device: it leaves the mirror
                lock (_gate)
                {
                    RemoveTarget(session.Id, mirror);
                }

                continue;
            }

            var selfCorrecting = now.Ticks - Volatile.Read(ref state.AskedAtTicks) < SelfCorrectingFor.Ticks;
            if (selfCorrecting || !CanDrift(source) || now - state.CorrectedAt < CorrectionGap)
            {
                continue;
            }

            var mine = PositionNow(session, now);
            if (sourcePosition is { } wanted && mine is { } have && Math.Abs(have - wanted) > DriftLimit.Ticks)
            {
                state.CorrectedAt = now;
                await Command(source, session, PlaystateCommand.Seek, wanted + Latency.Ticks, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task FollowChannelAsync(Mirror mirror, SessionInfo source, List<(SessionInfo Session, Follower State)> followers, string channelId, DateTime now, CancellationToken cancellationToken)
    {
        mirror.NothingSince = null;
        mirror.LastItem = null;
        if (!string.Equals(mirror.LastChannel, channelId, StringComparison.Ordinal))
        {
            mirror.LastChannel = channelId;
            foreach (var (_, state) in followers)
            {
                state.NeedsPlay = true;
            }
        }

        foreach (var (session, state) in followers.Where(f => f.State.NeedsPlay))
        {
            // The follower's app tunes to the channel when it gets this string; the channel's clock does the rest
            var command = new GeneralCommand { Name = GeneralCommandType.SendString, ControllingUserId = source.UserId };
            command.Arguments["String"] = ChannelCommandPrefix + channelId;
            await _sessions.SendGeneralCommand(source.Id, session.Id, command, cancellationToken).ConfigureAwait(false);
            state.NeedsPlay = false;
            state.PlayedAt = now;
        }
    }

    private Task Play(SessionInfo source, SessionInfo follower, Guid itemId, long? position, CancellationToken cancellationToken) =>
        _sessions.SendPlayCommand(
            source.Id,
            follower.Id,
            new PlayRequest
            {
                ItemIds = [itemId],
                PlayCommand = PlayCommand.PlayNow,
                StartPositionTicks = position,
                MediaSourceId = source.PlayState?.MediaSourceId,
                AudioStreamIndex = null,
                SubtitleStreamIndex = null,
                ControllingUserId = source.UserId
            },
            cancellationToken);

    private Task Command(SessionInfo source, SessionInfo follower, PlaystateCommand command, long? position, CancellationToken cancellationToken) =>
        _sessions.SendPlaystateCommand(
            source.Id,
            follower.Id,
            new PlaystateRequest
            {
                Command = command,
                SeekPositionTicks = position,
                ControllingUserId = source.UserId.ToString("N", CultureInfo.InvariantCulture)
            },
            cancellationToken);

    private sealed class Mirror
    {
        public Guid UserId { get; init; }

        public string SourceId { get; init; } = string.Empty;

        public Dictionary<string, Follower> Targets { get; } = new(StringComparer.Ordinal);

        public Guid? LastItem { get; set; }

        public string? LastChannel { get; set; }

        public bool LastPaused { get; set; }

        public DateTime? NothingSince { get; set; }

        public DateTime? MissingSince { get; set; }
    }

    private sealed record ChannelReport(string ChannelId, DateTime At);

    private sealed class Follower
    {
        // A field, not a property, so it can be read and written across threads
#pragma warning disable SA1401, CA1051
        public long AskedAtTicks;
#pragma warning restore SA1401, CA1051

        public bool NeedsPlay { get; set; }

        public DateTime PlayedAt { get; set; } = DateTime.MinValue;

        public DateTime CorrectedAt { get; set; } = DateTime.MinValue;
    }
}
