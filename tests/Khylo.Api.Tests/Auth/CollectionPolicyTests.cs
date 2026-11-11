using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Khylo.Api.Auth.DefaultAuthorizationPolicy;
using Khylo.Api.Auth.UserPermissionPolicy;
using Khylo.Api.Constants;
using Khylo.Api.Controllers;
using Khylo.Common.Api;
using Khylo.Common.Net;
using Khylo.Controller.Library;
using Khylo.Data;
using Khylo.Database.Implementations.Entities;
using Khylo.Database.Implementations.Enums;
using Khylo.Server.Implementations.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Khylo.Api.Tests.Auth;

public class CollectionPolicyTests
{
    private readonly Dictionary<Guid, User> _users = [];
    private readonly IAuthorizationService _authorization;

    public CollectionPolicyTests()
    {
        var userManager = new Mock<IUserManager>();
        userManager.Setup(m => m.GetUserById(It.IsAny<Guid>())).Returns((Guid id) => _users.GetValueOrDefault(id));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton<IAuthorizationHandler>(new DefaultAuthorizationHandler(userManager.Object, Mock.Of<INetworkManager>(), Mock.Of<IHttpContextAccessor>()));
        services.AddSingleton<IAuthorizationHandler>(new UserPermissionHandler(userManager.Object));
        services.AddAuthorizationCore(options =>
            options.AddPolicy(Policies.CollectionManagement, policy => policy.Requirements.Add(new UserPermissionRequirement(PermissionKind.EnableCollectionManagement))));
        _authorization = services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    [Fact]
    public void CollectionsAreGuardedByTheCollectionPermission()
    {
        var attribute = typeof(CollectionController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.Equal(Policies.CollectionManagement, attribute?.Policy);
    }

    [Fact]
    public async Task AnAdministrator_PassesWithoutTheFlag()
    {
        Assert.True(await Allowed(NewUser(admin: true, flag: false), admin: true));
    }

    [Fact]
    public async Task AUserWithThePermission_Passes()
    {
        Assert.True(await Allowed(NewUser(admin: false, flag: true), admin: false));
    }

    [Fact]
    public async Task AUserWithoutThePermission_AKidsProfile_IsRefused()
    {
        Assert.False(await Allowed(NewUser(admin: false, flag: false), admin: false));
    }

    private User NewUser(bool admin, bool flag)
    {
        var user = new User("someone", typeof(DefaultAuthenticationProvider).FullName!, typeof(DefaultPasswordResetProvider).FullName!) { Id = Guid.NewGuid() };
        user.AddDefaultPermissions();
        user.AddDefaultPreferences();
        user.SetPermission(PermissionKind.IsAdministrator, admin);
        user.SetPermission(PermissionKind.EnableCollectionManagement, flag);
        _users[user.Id] = user;
        return user;
    }

    private async Task<bool> Allowed(User user, bool admin)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Role, admin ? UserRoles.Administrator : UserRoles.User),
            new Claim(InternalClaimTypes.UserId, user.Id.ToString("N")),
            new Claim(InternalClaimTypes.IsApiKey, "false")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return (await _authorization.AuthorizeAsync(principal, null, Policies.CollectionManagement)).Succeeded;
    }
}
