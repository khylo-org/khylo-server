using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Khylo.Api.Constants;
using Khylo.Api.Controllers;
using Khylo.Api.Helpers;
using Khylo.Api.Models.HomeProfileDtos;
using Khylo.Common.Configuration;
using Khylo.Controller.Authentication;
using Khylo.Controller.Library;
using Khylo.Controller.Session;
using Khylo.Data;
using Khylo.Database.Implementations.Entities;
using Khylo.Database.Implementations.Enums;
using Khylo.Model.Dto;
using Khylo.Model.Globalization;
using Khylo.Server.Implementations.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Khylo.Api.Tests.Controllers;

public sealed class HomeProfilesControllerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "khylo-profile-tests-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<Guid, User> _users = [];
    private readonly Mock<IUserManager> _userManager = new();
    private readonly Mock<ISessionManager> _sessions = new();
    private readonly HomeProfileStore _store;
    private readonly User _owner;

    public HomeProfilesControllerTests()
    {
        Directory.CreateDirectory(_dir);
        _store = new HomeProfileStore(_dir);
        _userManager.Setup(m => m.GetUserById(It.IsAny<Guid>())).Returns((Guid id) => _users.GetValueOrDefault(id));
        _userManager.Setup(m => m.GetUserDto(It.IsAny<User>(), It.IsAny<string?>())).Returns(new UserDto());
        _sessions.Setup(m => m.AuthenticateDirect(It.IsAny<AuthenticationRequest>())).ReturnsAsync(new AuthenticationResult());
        _owner = NewUser("owner", admin: true);
    }

    [Fact]
    public async Task AdultProfile_CannotEnterAnAdministratorWithoutAPin()
    {
        var adult = NewProfile("adult");

        var result = await Controller(adult).SwitchProfile(_owner.Id, new SwitchHomeProfileRequest());

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        Entered(Times.Never());
    }

    [Fact]
    public async Task AdultProfile_CannotEnterAnAdministratorWithTheWrongPin()
    {
        var adult = NewProfile("adult");
        _store.SetPin(_owner.Id, "4821");

        var result = await Controller(adult).SwitchProfile(_owner.Id, new SwitchHomeProfileRequest { Pin = "1111" });

        Assert.IsType<ForbidResult>(result.Result);
        Entered(Times.Never());
    }

    [Fact]
    public async Task AdultProfile_EntersAnAdministratorWithTheRightPin()
    {
        var adult = NewProfile("adult");
        _store.SetPin(_owner.Id, "4821");

        var result = await Controller(adult).SwitchProfile(_owner.Id, new SwitchHomeProfileRequest { Pin = "4821" });

        Assert.NotNull(result.Value);
        Entered(Times.Once());
    }

    [Fact]
    public async Task ProfileMadeAdministratorLater_NeedsItsPinToo()
    {
        var adult = NewProfile("adult");
        var other = NewProfile("other");
        other.SetPermission(PermissionKind.IsAdministrator, true);

        var result = await Controller(adult).SwitchProfile(other.Id, new SwitchHomeProfileRequest());

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        Entered(Times.Never());
    }

    [Fact]
    public async Task KidsProfile_StillCannotEnterAnAdultWithoutAPin()
    {
        var parent = NewUser("parent", admin: false);
        var kid = NewUser("kid", admin: false);
        _store.AddProfile(parent.Id, kid.Id);
        _store.MarkKids(kid.Id);

        var result = await Controller(kid).SwitchProfile(parent.Id, new SwitchHomeProfileRequest());

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        Entered(Times.Never());
    }

    [Fact]
    public async Task AdultProfile_StillEntersAFamilyMemberWithoutAPin()
    {
        var parent = NewUser("parent", admin: false);
        var adult = NewUser("adult", admin: false);
        _store.AddProfile(parent.Id, adult.Id);

        var result = await Controller(adult).SwitchProfile(parent.Id, new SwitchHomeProfileRequest());

        Assert.NotNull(result.Value);
        Entered(Times.Once());
    }

    [Fact]
    public void Profiles_SaysAnAdministratorWithoutAPinNeedsAGrownUp()
    {
        var adult = NewProfile("adult");

        var profiles = Controller(adult).GetProfiles().Value!.Profiles;

        Assert.True(profiles.Single(p => p.Id.Equals(_owner.Id)).NeedsGrownUp);
        Assert.False(profiles.Single(p => p.Id.Equals(adult.Id)).NeedsGrownUp);

        _store.SetPin(_owner.Id, "4821");
        profiles = Controller(adult).GetProfiles().Value!.Profiles;
        Assert.False(profiles.Single(p => p.Id.Equals(_owner.Id)).NeedsGrownUp);
    }

    [Theory]
    [InlineData("1234", true)]
    [InlineData("12345678", true)]
    [InlineData("123", false)]
    [InlineData("123456789", false)]
    [InlineData("12a4", false)]
    [InlineData("12 4", false)]
    [InlineData("١٢٣٤", false)]
    public void SetPin_AcceptsFourToEightDigits(string pin, bool accepted)
    {
        var adult = NewProfile("adult");

        var result = Controller(adult).SetPin(adult.Id, new SwitchHomeProfileRequest { Pin = pin });

        Assert.Equal(accepted, result is NoContentResult);
        Assert.Equal(accepted, _store.HasPin(adult.Id));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
            // A temporary folder that could not be removed is not a failed test
        }
    }

    private void Entered(Times times)
        => _sessions.Verify(m => m.AuthenticateDirect(It.IsAny<AuthenticationRequest>()), times);

    private User NewUser(string name, bool admin)
    {
        var user = new User(name, typeof(DefaultAuthenticationProvider).FullName!, typeof(DefaultPasswordResetProvider).FullName!) { Id = Guid.NewGuid() };
        user.AddDefaultPermissions();
        user.AddDefaultPreferences();
        user.SetPermission(PermissionKind.IsAdministrator, admin);
        _users[user.Id] = user;
        return user;
    }

    // A profile in the administrator's home
    private User NewProfile(string name, bool kids = false)
    {
        var profile = NewUser(name, admin: false);
        _store.AddProfile(_owner.Id, profile.Id);
        if (kids)
        {
            _store.MarkKids(profile.Id);
        }

        return profile;
    }

    private HomeProfilesController Controller(User signedIn)
    {
        var paths = new Mock<IApplicationPaths>();
        paths.SetupGet(p => p.ConfigurationDirectoryPath).Returns(_dir);
        var localization = new Mock<ILocalizationManager>();
        localization.Setup(l => l.GetParentalRatings()).Returns(Array.Empty<Khylo.Model.Entities.ParentalRating>());

        var claims = new List<Claim>
        {
            new(InternalClaimTypes.UserId, signedIn.Id.ToString("N")),
            new(InternalClaimTypes.DeviceId, "test-device"),
            new(InternalClaimTypes.Client, "test"),
            new(InternalClaimTypes.Device, "test"),
            new(InternalClaimTypes.Version, "1")
        };
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");
        return new HomeProfilesController(_userManager.Object, _sessions.Object, paths.Object, localization.Object, Mock.Of<ILibraryManager>())
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }
}
