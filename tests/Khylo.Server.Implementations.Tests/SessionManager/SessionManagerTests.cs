using System;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Controller;
using Khylo.Controller.Configuration;
using Khylo.Controller.Devices;
using Khylo.Controller.Drawing;
using Khylo.Controller.Dto;
using Khylo.Controller.Events;
using Khylo.Controller.Library;
using Khylo.Controller.Net;
using Khylo.Controller.Session;
using Khylo.Data;
using Khylo.Database.Implementations.Entities;
using Khylo.Database.Implementations.Enums;
using Khylo.Model.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Khylo.Server.Implementations.Tests.SessionManager;

public class SessionManagerTests
{
    [Theory]
    [InlineData("", typeof(ArgumentException))]
    [InlineData(null, typeof(ArgumentNullException))]
    public async Task GetAuthorizationToken_Should_ThrowException(string? deviceId, Type exceptionType)
    {
        await using var sessionManager = new Khylo.Server.Core.Session.SessionManager(
            NullLogger<Khylo.Server.Core.Session.SessionManager>.Instance,
            Mock.Of<IEventManager>(),
            Mock.Of<IUserDataManager>(),
            Mock.Of<IServerConfigurationManager>(),
            Mock.Of<ILibraryManager>(),
            Mock.Of<IUserManager>(),
            Mock.Of<IMusicManager>(),
            Mock.Of<IDtoService>(),
            Mock.Of<IImageProcessor>(),
            Mock.Of<IServerApplicationHost>(),
            Mock.Of<IDeviceManager>(),
            Mock.Of<IMediaSourceManager>(),
            Mock.Of<IHostApplicationLifetime>());

        await Assert.ThrowsAsync(exceptionType, () => sessionManager.GetAuthorizationToken(
            new User("test", "default", "default"),
            deviceId,
            "app_name",
            "0.0.0",
            "device_name"));
    }

    [Theory]
    [MemberData(nameof(AuthenticateNewSessionInternal_Exception_TestData))]
    public async Task AuthenticateNewSessionInternal_Should_ThrowException(AuthenticationRequest authenticationRequest, Type exceptionType)
    {
        await using var sessionManager = new Khylo.Server.Core.Session.SessionManager(
            NullLogger<Khylo.Server.Core.Session.SessionManager>.Instance,
            Mock.Of<IEventManager>(),
            Mock.Of<IUserDataManager>(),
            Mock.Of<IServerConfigurationManager>(),
            Mock.Of<ILibraryManager>(),
            Mock.Of<IUserManager>(),
            Mock.Of<IMusicManager>(),
            Mock.Of<IDtoService>(),
            Mock.Of<IImageProcessor>(),
            Mock.Of<IServerApplicationHost>(),
            Mock.Of<IDeviceManager>(),
            Mock.Of<IMediaSourceManager>(),
            Mock.Of<IHostApplicationLifetime>());

        await Assert.ThrowsAsync(exceptionType, () => sessionManager.AuthenticateNewSessionInternal(authenticationRequest, false));
    }

    public static TheoryData<AuthenticationRequest, Type> AuthenticateNewSessionInternal_Exception_TestData()
    {
        var data = new TheoryData<AuthenticationRequest, Type>
        {
            {
                new AuthenticationRequest { App = string.Empty, DeviceId = "device_id", DeviceName = "device_name", AppVersion = "app_version" },
                typeof(ArgumentException)
            },
            {
                new AuthenticationRequest { App = null, DeviceId = "device_id", DeviceName = "device_name", AppVersion = "app_version" },
                typeof(ArgumentNullException)
            },
            {
                new AuthenticationRequest { App = "app_name", DeviceId = string.Empty, DeviceName = "device_name", AppVersion = "app_version" },
                typeof(ArgumentException)
            },
            {
                new AuthenticationRequest { App = "app_name", DeviceId = null, DeviceName = "device_name", AppVersion = "app_version" },
                typeof(ArgumentNullException)
            },
            {
                new AuthenticationRequest { App = "app_name", DeviceId = "device_id", DeviceName = string.Empty, AppVersion = "app_version" },
                typeof(ArgumentException)
            },
            {
                new AuthenticationRequest { App = "app_name", DeviceId = "device_id", DeviceName = null, AppVersion = "app_version" },
                typeof(ArgumentNullException)
            },
            {
                new AuthenticationRequest { App = "app_name", DeviceId = "device_id", DeviceName = "device_name", AppVersion = string.Empty },
                typeof(ArgumentException)
            },
            {
                new AuthenticationRequest { App = "app_name", DeviceId = "device_id", DeviceName = "device_name", AppVersion = null },
                typeof(ArgumentNullException)
            }
        };

        return data;
    }

    [Fact]
    public async Task SendMessageCommand_Should_ThrowSecurityException_WhenControllingAnotherUsersSession()
    {
        var victim = new User("victim", "default", "default");
        var attacker = new User("attacker", "default", "default");
        await using var sessionManager = CreateSessionManager(victim, attacker);

        var victimSession = await LogSessionActivity(sessionManager, victim);
        var attackerSession = await LogSessionActivity(sessionManager, attacker);

        await Assert.ThrowsAsync<SecurityException>(() => sessionManager.SendMessageCommand(
            attackerSession.Id,
            victimSession.Id,
            new MessageCommand { Header = "Custom Message", Text = "test exploit!" },
            CancellationToken.None));
    }

    [Fact]
    public async Task SendMessageCommand_Should_Succeed_WhenAllowedToControlOtherUsers()
    {
        var victim = new User("victim", "default", "default");
        var attacker = new User("controller", "default", "default");
        attacker.SetPermission(PermissionKind.EnableRemoteControlOfOtherUsers, true);
        await using var sessionManager = CreateSessionManager(victim, attacker);

        var victimSession = await LogSessionActivity(sessionManager, victim);
        var controllingSession = await LogSessionActivity(sessionManager, attacker);

        await sessionManager.SendMessageCommand(
            controllingSession.Id,
            victimSession.Id,
            new MessageCommand { Header = "Custom Message", Text = "hello" },
            CancellationToken.None);
    }

    [Fact]
    public async Task LogSessionActivity_Should_NotReuseAnotherUsersSession()
    {
        var victim = new User("victim", "default", "default");
        var attacker = new User("attacker", "default", "default");
        await using var sessionManager = CreateSessionManager(victim, attacker);

        // Client name and device id are attacker controlled, so they must not identify a session on their own.
        var victimSession = await LogSessionActivity(sessionManager, victim);
        var attackerSession = await LogSessionActivity(sessionManager, attacker);

        Assert.NotEqual(victimSession.Id, attackerSession.Id);
        Assert.Equal(victim.Id, victimSession.UserId);
    }

    [Fact]
    public async Task AddAdditionalUser_Should_ThrowSecurityException_WhenAttachingAnotherUser()
    {
        var attacker = new User("attacker", "default", "default");
        var victim = new User("victim", "default", "default");
        await using var sessionManager = CreateSessionManager(victim, attacker);

        var attackerSession = await LogSessionActivity(sessionManager, attacker);

        Assert.Throws<SecurityException>(() => sessionManager.AddAdditionalUser(attackerSession.Id, attackerSession.Id, victim.Id));
    }

    [Fact]
    public async Task AddAdditionalUser_Should_Succeed_WhenCallerIsAdministrator()
    {
        var admin = new User("admin", "default", "default");
        admin.SetPermission(PermissionKind.IsAdministrator, true);
        var guest = new User("guest", "default", "default");
        await using var sessionManager = CreateSessionManager(admin, guest);

        var adminSession = await LogSessionActivity(sessionManager, admin);

        sessionManager.AddAdditionalUser(adminSession.Id, adminSession.Id, guest.Id);

        Assert.Contains(adminSession.AdditionalUsers, i => i.UserId.Equals(guest.Id));
    }

    [Fact]
    public async Task RemoveAdditionalUser_Should_ThrowSecurityException_WhenModifyingAnotherUsersSession()
    {
        var victim = new User("victim", "default", "default");
        var attacker = new User("attacker", "default", "default");
        await using var sessionManager = CreateSessionManager(victim, attacker);

        var victimSession = await LogSessionActivity(sessionManager, victim);
        var attackerSession = await LogSessionActivity(sessionManager, attacker);

        Assert.Throws<SecurityException>(() => sessionManager.RemoveAdditionalUser(attackerSession.Id, victimSession.Id, attacker.Id));
    }

    [Fact]
    public async Task ReportCapabilities_Should_ThrowSecurityException_WhenReportingForAnotherUsersSession()
    {
        var victim = new User("victim", "default", "default");
        var attacker = new User("attacker", "default", "default");
        await using var sessionManager = CreateSessionManager(victim, attacker);

        var victimSession = await LogSessionActivity(sessionManager, victim);
        var attackerSession = await LogSessionActivity(sessionManager, attacker);

        Assert.Throws<SecurityException>(() => sessionManager.ReportCapabilities(attackerSession.Id, victimSession.Id, new ClientCapabilities()));
    }

    private static Khylo.Server.Core.Session.SessionManager CreateSessionManager(params User[] users)
    {
        var userManager = new Mock<IUserManager>();
        foreach (var user in users)
        {
            userManager.Setup(i => i.GetUserById(user.Id)).Returns(user);
        }

        return new Khylo.Server.Core.Session.SessionManager(
            NullLogger<Khylo.Server.Core.Session.SessionManager>.Instance,
            Mock.Of<IEventManager>(),
            Mock.Of<IUserDataManager>(),
            Mock.Of<IServerConfigurationManager>(),
            Mock.Of<ILibraryManager>(),
            userManager.Object,
            Mock.Of<IMusicManager>(),
            Mock.Of<IDtoService>(),
            Mock.Of<IImageProcessor>(),
            Mock.Of<IServerApplicationHost>(),
            Mock.Of<IDeviceManager>(),
            Mock.Of<IMediaSourceManager>(),
            Mock.Of<IHostApplicationLifetime>());
    }

    // All sessions are logged with the same client and device id on purpose, those values are taken
    // from the request headers and are not bound to the access token of the calling user.
    private static Task<SessionInfo> LogSessionActivity(ISessionManager sessionManager, User user)
        => sessionManager.LogSessionActivity("Khylo Web", "1.0.0", "victim-tv-01", "device_name", "127.0.0.1", user);
}
