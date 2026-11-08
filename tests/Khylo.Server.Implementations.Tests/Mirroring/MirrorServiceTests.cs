using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Controller.Session;
using Khylo.Model.Dto;
using Khylo.Model.Session;
using Khylo.Server.Core.Mirroring;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Khylo.Server.Implementations.Tests.Mirroring;

public sealed class MirrorServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 6, 20, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Person = Guid.NewGuid();
    private static readonly Guid Movie = Guid.NewGuid();

    private readonly Mock<ISessionManager> _manager = new();
    private readonly List<SessionInfo> _sessions = [];
    private readonly MirrorService _service;

    public MirrorServiceTests()
    {
        _manager.SetupGet(m => m.Sessions).Returns(_sessions);
        _service = new MirrorService(_manager.Object, NullLogger<MirrorService>.Instance);
    }

    public void Dispose() => _service.Dispose();

    private SessionInfo Device(string id, bool controllable = true)
    {
        var controller = new Mock<ISessionController>();
        controller.SetupGet(c => c.IsSessionActive).Returns(true);
        controller.SetupGet(c => c.SupportsMediaControl).Returns(controllable);
        var session = new SessionInfo(_manager.Object, NullLogger.Instance)
        {
            Id = id,
            UserId = Person,
            DeviceId = id + "-device",
            DeviceName = id,
            Client = "Khylo TV",
            Capabilities = new ClientCapabilities { SupportsMediaControl = controllable },
            SessionControllers = [controller.Object]
        };
        _sessions.Add(session);
        return session;
    }

    private static void Play(SessionInfo session, Guid item, double seconds, bool paused = false, DateTime? reported = null)
    {
        session.NowPlayingItem = new BaseItemDto { Id = item, Name = "Movie", RunTimeTicks = TimeSpan.FromHours(2).Ticks };
        session.PlayState = new PlayerStateInfo { PositionTicks = TimeSpan.FromSeconds(seconds).Ticks, IsPaused = paused, CanSeek = true };
        session.LastPlaybackCheckIn = reported ?? Now;
    }

    private Task Tick(DateTime at) => _service.TickAsync(at, CancellationToken.None);

    private void VerifyPlayed(string target, Times times) =>
        _manager.Verify(m => m.SendPlayCommand(It.IsAny<string>(), target, It.Is<PlayRequest>(r => r.ItemIds[0].Equals(Movie)), It.IsAny<CancellationToken>()), times);

    private void VerifyState(string target, PlaystateCommand command, Times times) =>
        _manager.Verify(m => m.SendPlaystateCommand(It.IsAny<string>(), target, It.Is<PlaystateRequest>(r => r.Command == command), It.IsAny<CancellationToken>()), times);

    [Fact]
    public async Task Follower_StartsWhatTheFollowedDevicePlays_FromTheSameMoment()
    {
        var source = Device("living");
        Device("bedroom");
        Play(source, Movie, 600);

        _service.Start(Person, "living", ["bedroom"]);
        await Tick(Now);

        _manager.Verify(
            m => m.SendPlayCommand(
                "living",
                "bedroom",
                It.Is<PlayRequest>(r => r.ItemIds[0].Equals(Movie) && r.StartPositionTicks == TimeSpan.FromSeconds(600).Ticks),
                It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Follower_IsNotToldToPlayAgainOnLaterBeats()
    {
        var source = Device("living");
        var follower = Device("bedroom");
        Play(source, Movie, 600);
        _service.Start(Person, "living", ["bedroom"]);
        await Tick(Now);
        Play(follower, Movie, 600);

        await Tick(Now.AddSeconds(1));
        await Tick(Now.AddSeconds(2));

        VerifyPlayed("bedroom", Times.Once());
    }

    [Fact]
    public async Task Pause_AndResume_FollowTheFollowedDevice()
    {
        var source = Device("living");
        var follower = Device("bedroom");
        Play(source, Movie, 600);
        _service.Start(Person, "living", ["bedroom"]);
        await Tick(Now);
        Play(follower, Movie, 600);

        Play(source, Movie, 610, true, Now.AddSeconds(10));
        await Tick(Now.AddSeconds(10));
        VerifyState("bedroom", PlaystateCommand.Pause, Times.Once());

        Play(source, Movie, 610, false, Now.AddSeconds(20));
        await Tick(Now.AddSeconds(20));
        VerifyState("bedroom", PlaystateCommand.Unpause, Times.Once());
    }

    [Fact]
    public async Task Follower_ThatDriftsMoreThanTwoSeconds_IsSeekedBack_AfterItSettles()
    {
        var source = Device("living");
        var follower = Device("bedroom");
        Play(source, Movie, 600);
        _service.Start(Person, "living", ["bedroom"]);
        await Tick(Now);

        var later = Now.AddSeconds(30);
        Play(source, Movie, 630, false, later);
        Play(follower, Movie, 625, false, later);
        await Tick(later);

        _manager.Verify(
            m => m.SendPlaystateCommand("living", "bedroom", It.Is<PlaystateRequest>(r => r.Command == PlaystateCommand.Seek && r.SeekPositionTicks > TimeSpan.FromSeconds(630).Ticks), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Follower_WithinTheLimit_IsLeftAlone()
    {
        var source = Device("living");
        var follower = Device("bedroom");
        Play(source, Movie, 600);
        _service.Start(Person, "living", ["bedroom"]);
        await Tick(Now);

        var later = Now.AddSeconds(30);
        Play(source, Movie, 630, false, later);
        Play(follower, Movie, 629.5, false, later);
        await Tick(later);

        VerifyState("bedroom", PlaystateCommand.Seek, Times.Never());
    }

    [Fact]
    public async Task Follower_ThatCorrectsItself_IsNotJumpedByTheServer()
    {
        var source = Device("living");
        var follower = Device("bedroom");
        Play(source, Movie, 600);
        _service.Start(Person, "living", ["bedroom"]);
        await Tick(DateTime.UtcNow.AddMinutes(-1));

        var later = DateTime.UtcNow;
        Play(source, Movie, 630, false, later);
        Play(follower, Movie, 625, false, later);
        Assert.NotNull(_service.ReferenceFor(Person, "bedroom-device"));
        await Tick(later.AddSeconds(1));

        VerifyState("bedroom", PlaystateCommand.Seek, Times.Never());
    }

    [Fact]
    public void Reference_SaysWhereTheFollowedDeviceIs_OnlyToItsFollowers()
    {
        var source = Device("living");
        Device("bedroom");
        Device("study");
        Play(source, Movie, 600, false, DateTime.UtcNow);
        _service.Start(Person, "living", ["bedroom"]);

        var reference = _service.ReferenceFor(Person, "bedroom-device");

        Assert.NotNull(reference);
        Assert.Equal(Movie, reference.ItemId);
        Assert.InRange(reference.PositionTicks, TimeSpan.FromSeconds(600).Ticks, TimeSpan.FromSeconds(602).Ticks);
        Assert.Null(_service.ReferenceFor(Person, "study-device"));
        Assert.Null(_service.ReferenceFor(Person, "living-device"));
        Assert.Null(_service.ReferenceFor(Guid.NewGuid(), "bedroom-device"));
    }

    [Fact]
    public async Task Follower_WhoseConnectionIsDownForAMoment_JoinsWhenItIsBack()
    {
        var source = Device("living");
        var follower = Device("bedroom");
        Play(source, Movie, 600);
        var down = new Mock<ISessionController>();
        down.SetupGet(c => c.IsSessionActive).Returns(false);
        down.SetupGet(c => c.SupportsMediaControl).Returns(true);
        var up = follower.SessionControllers;
        follower.SessionControllers = [down.Object];

        // Starting is allowed, and nothing is sent while the follower cannot be reached
        _service.Start(Person, "living", ["bedroom"]);
        await Tick(Now);
        _manager.Verify(m => m.SendPlayCommand(It.IsAny<string>(), "bedroom", It.IsAny<PlayRequest>(), It.IsAny<CancellationToken>()), Times.Never());

        follower.SessionControllers = up;
        await Tick(Now.AddSeconds(1));
        _manager.Verify(m => m.SendPlayCommand("living", "bedroom", It.IsAny<PlayRequest>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task KhyloTvChannel_IsFollowed_ByTuningTheFollowerToTheSameChannel()
    {
        Device("living");
        Device("bedroom");
        _service.Start(Person, "living", ["bedroom"]);
        _service.SetChannel(Person, "living-device", "channel-7");

        await Tick(Now);
        await Tick(Now.AddSeconds(2));

        // Told once, not every second
        _manager.Verify(
            m => m.SendGeneralCommand(
                "living",
                "bedroom",
                It.Is<GeneralCommand>(c => c.Name == GeneralCommandType.SendString && c.Arguments["String"] == "khylo-tv-channel:channel-7"),
                It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task KhyloTvChannel_ChangedOnTheFollowedDevice_IsSentAgain()
    {
        Device("living");
        Device("bedroom");
        _service.Start(Person, "living", ["bedroom"]);
        _service.SetChannel(Person, "living-device", "channel-7");
        await Tick(Now);

        _service.SetChannel(Person, "living-device", "channel-9");
        await Tick(Now.AddSeconds(1));

        _manager.Verify(
            m => m.SendGeneralCommand("living", "bedroom", It.Is<GeneralCommand>(c => c.Arguments["String"] == "khylo-tv-channel:channel-9"), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task KhyloTvChannel_ThatWasLeft_IsNotFollowed()
    {
        Device("living");
        Device("bedroom");
        _service.Start(Person, "living", ["bedroom"]);
        _service.SetChannel(Person, "living-device", "channel-7");
        _service.SetChannel(Person, "living-device", null);

        await Tick(Now);

        _manager.Verify(m => m.SendGeneralCommand(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GeneralCommand>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Follower_TheViewerStopped_LeavesTheMirror()
    {
        var source = Device("living");
        var follower = Device("bedroom");
        Play(source, Movie, 600);
        _service.Start(Person, "living", ["bedroom"]);
        await Tick(Now);
        follower.NowPlayingItem = null;

        await Tick(Now.AddSeconds(30));

        Assert.Empty(_service.List(Person));
    }

    [Fact]
    public async Task WhenTheFollowedDeviceStops_FollowersStopAfterAShortGap()
    {
        var source = Device("living");
        var follower = Device("bedroom");
        Play(source, Movie, 600);
        _service.Start(Person, "living", ["bedroom"]);
        await Tick(Now);
        Play(follower, Movie, 600);

        source.NowPlayingItem = null;
        await Tick(Now.AddSeconds(2));
        VerifyState("bedroom", PlaystateCommand.Stop, Times.Never());

        await Tick(Now.AddSeconds(8));
        VerifyState("bedroom", PlaystateCommand.Stop, Times.Once());
        Assert.Single(_service.List(Person));
    }

    [Fact]
    public void Start_RefusesDevicesThatCantBeControlled_AndOtherPeoplesDevices()
    {
        Device("living");
        Device("old", false);
        var other = Device("theirs");
        other.UserId = Guid.NewGuid();

        Assert.Throws<InvalidOperationException>(() => _service.Start(Person, "living", ["old"]));
        Assert.Throws<InvalidOperationException>(() => _service.Start(Person, "living", ["theirs"]));
        Assert.Throws<InvalidOperationException>(() => _service.Start(Person, "living", ["living"]));
        Assert.Throws<InvalidOperationException>(() => _service.Start(Person, "gone", ["old"]));
    }

    [Fact]
    public void Stop_EndsAMirror_OrTakesOneFollowerOut()
    {
        Device("living");
        Device("bedroom");
        Device("kitchen");
        _service.Start(Person, "living", ["bedroom", "kitchen"]);

        Assert.True(_service.Stop(Person, "kitchen"));
        Assert.Single(Assert.Single(_service.List(Person)).Targets);
        Assert.True(_service.Stop(Person, "living"));
        Assert.Empty(_service.List(Person));
        Assert.False(_service.Stop(Person, "living"));
    }

    [Fact]
    public void Stop_WontTouchAnotherPersonsMirror()
    {
        Device("living");
        Device("bedroom");
        _service.Start(Person, "living", ["bedroom"]);

        Assert.False(_service.Stop(Guid.NewGuid(), "living"));
        Assert.Single(_service.List(Person));
    }
}
