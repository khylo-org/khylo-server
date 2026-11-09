using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Khylo.Api.Constants;
using Khylo.Api.Controllers;
using Khylo.Controller.Library;
using Khylo.Controller.Mirroring;
using Khylo.Controller.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Khylo.Api.Tests.Controllers;

public class KhyloMirrorControllerTests
{
    private static readonly Guid Person = Guid.NewGuid();
    private readonly List<SessionInfo> _sessions = [];
    private readonly Mock<IMirrorService> _mirrors = new();
    private readonly Mock<ISessionManager> _manager = new();

    public KhyloMirrorControllerTests()
    {
        _manager.SetupGet(m => m.Sessions).Returns(_sessions);
        _mirrors
            .Setup(m => m.Start(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>()))
            .Returns(new MirrorInfo());
    }

    [Fact]
    public async Task Start_UsesTheSessionWithTheSameDeviceId()
    {
        Session("tv-session", "tv-device", "Khylo TV", "10.13.1.92");
        Session("phone-session", "phone-device", "Khylo iOS", "10.11.1.56");

        await Controller("tv-device", "Khylo TV", "10.13.1.92").Start(new MirrorRequest { TargetSessionIds = ["phone-session"] });

        _mirrors.Verify(m => m.Start(Person, "tv-session", It.IsAny<IReadOnlyCollection<string>>()), Times.Once());
    }

    [Fact]
    public async Task Start_FindsTheDeviceByAppAndAddress_WhenNoSessionHasItsDeviceId()
    {
        // The request names the device one way, the session was opened under another: the same app from the same address is the same TV
        Session("tv-session", "tv-device-as-the-socket-named-it", "Khylo TV", "10.13.1.92");
        Session("phone-session", "phone-device", "Khylo iOS", "10.11.1.56");

        await Controller("tv-device-as-the-token-names-it", "Khylo TV", "10.13.1.92").Start(new MirrorRequest { TargetSessionIds = ["phone-session"] });

        _mirrors.Verify(m => m.Start(Person, "tv-session", It.IsAny<IReadOnlyCollection<string>>()), Times.Once());
    }

    [Fact]
    public async Task Start_DoesNotUseAnotherPersonsSession()
    {
        var other = Session("their-tv", "tv-device", "Khylo TV", "10.13.1.92");
        other.UserId = Guid.NewGuid();

        var result = await Controller(string.Empty, "Khylo TV", "10.13.1.92").Start(new MirrorRequest { TargetSessionIds = ["phone-session"] });

        _mirrors.Verify(m => m.Start(It.IsAny<Guid>(), "their-tv", It.IsAny<IReadOnlyCollection<string>>()), Times.Never());
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    private SessionInfo Session(string id, string deviceId, string client, string address)
    {
        var session = new SessionInfo(_manager.Object, NullLogger.Instance)
        {
            Id = id,
            UserId = Person,
            DeviceId = deviceId,
            DeviceName = id,
            Client = client,
            RemoteEndPoint = address,
            LastActivityDate = DateTime.UtcNow
        };
        _sessions.Add(session);
        return session;
    }

    private KhyloMirrorController Controller(string deviceId, string client, string address)
    {
        var claims = new List<Claim>
        {
            new(InternalClaimTypes.UserId, Person.ToString("N")),
            new(InternalClaimTypes.DeviceId, deviceId),
            new(InternalClaimTypes.Client, client)
        };
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);
        return new KhyloMirrorController(_mirrors.Object, _manager.Object, Mock.Of<IUserManager>(), NullLogger<KhyloMirrorController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }
}
