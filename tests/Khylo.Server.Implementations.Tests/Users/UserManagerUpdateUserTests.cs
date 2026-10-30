using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Common;
using Khylo.Common.Net;
using Khylo.Controller;
using Khylo.Controller.Configuration;
using Khylo.Controller.Drawing;
using Khylo.Controller.Events;
using Khylo.Data;
using Khylo.Database.Implementations;
using Khylo.Database.Implementations.Entities;
using Khylo.Database.Implementations.Enums;
using Khylo.Database.Implementations.Locking;
using Khylo.Database.Providers.Sqlite;
using Khylo.Model.Cryptography;
using Khylo.Model.Users;
using Khylo.Server.Implementations.Users;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Khylo.Server.Implementations.Tests.Users;

public sealed class UserManagerUpdateUserTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<KhyloDbContext> _dbOptions;
    private readonly UserManager _userManager;

    public UserManagerUpdateUserTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _dbOptions = new DbContextOptionsBuilder<KhyloDbContext>()
            .UseSqlite(_connection)
            .Options;

        // Create the schema
        using var ctx = CreateDbContext();
        ctx.Database.EnsureCreated();

        var factory = new Mock<IDbContextFactory<KhyloDbContext>>();
        factory.Setup(f => f.CreateDbContext()).Returns(CreateDbContext);
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDbContext);

        var cryptoProvider = new Mock<ICryptoProvider>();
        var configManager = new Mock<IServerConfigurationManager>();
        var appPaths = new Mock<IServerApplicationPaths>();
        appPaths.Setup(x => x.ProgramDataPath).Returns(Path.GetTempPath());
        configManager.Setup(x => x.ApplicationPaths).Returns(appPaths.Object);

        var appHost = new Mock<IApplicationHost>();

        var defaultAuthProvider = new DefaultAuthenticationProvider(
            NullLogger<DefaultAuthenticationProvider>.Instance,
            cryptoProvider.Object);
        var invalidAuthProvider = new InvalidAuthProvider();
        var defaultPasswordResetProvider = new DefaultPasswordResetProvider(
            configManager.Object,
            appHost.Object);

        _userManager = new UserManager(
            factory.Object,
            new NoopEventManager(),
            new Mock<INetworkManager>().Object,
            appHost.Object,
            new Mock<IImageProcessor>().Object,
            NullLogger<UserManager>.Instance,
            configManager.Object,
            [defaultPasswordResetProvider],
            [defaultAuthProvider, invalidAuthProvider]);
    }

    public void Dispose()
    {
        _userManager.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task UpdateUserAsync_DoesNotDetachPermissionsOrPreferences()
    {
        var user = await _userManager.CreateUserAsync("orphanuser");
        var permissionCount = user.Permissions.Count;
        var preferenceCount = user.Preferences.Count;

        user.LastActivityDate = DateTime.UtcNow;
        await _userManager.UpdateUserAsync(user);
        await _userManager.UpdateUserAsync(user);

        await using var context = CreateDbContext();
        Assert.Equal(permissionCount, await context.Permissions.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(preferenceCount, await context.Preferences.CountAsync(TestContext.Current.CancellationToken));
        Assert.All(
            await context.Permissions.ToListAsync(TestContext.Current.CancellationToken),
            permission => Assert.Equal(user.Id, permission.UserId));
        Assert.All(
            await context.Preferences.ToListAsync(TestContext.Current.CancellationToken),
            preference => Assert.Equal(user.Id, preference.UserId));
    }

    [Fact]
    public async Task UpdateUserAsync_WhenOnlyTheUserRowChanged_LeavesChildRowsUntouched()
    {
        var user = await _userManager.CreateUserAsync("churnuser");
        var before = await ReadChildRowsAsync();

        // A session activity stamp goes through the same path. It must not rewrite all 37 child
        // rows, which is what tearing the collections down and rebuilding them used to do.
        user.LastActivityDate = DateTime.UtcNow;
        await _userManager.UpdateUserAsync(user);

        Assert.Equal(before, await ReadChildRowsAsync());
    }

    [Fact]
    public async Task UpdatePolicyAsync_RaisesOnUserUpdated()
    {
        var user = await _userManager.CreateUserAsync("policyeventuser");

        User? updated = null;
        _userManager.OnUserUpdated += (_, e) => updated = e.Argument;

        await _userManager.UpdatePolicyAsync(
            user.Id,
            new UserPolicy
            {
                EnableAllDevices = false,
                AuthenticationProviderId = user.AuthenticationProviderId,
                PasswordResetProviderId = user.PasswordResetProviderId
            });

        Assert.NotNull(updated);
        Assert.Equal(user.Id, updated.Id);
    }

    [Fact]
    public async Task UpdateUserAsync_AppliesPermissionAndPreferenceChanges()
    {
        var user = await _userManager.CreateUserAsync("policyuser");
        Assert.False(user.HasPermission(PermissionKind.IsAdministrator));

        user.SetPermission(PermissionKind.IsAdministrator, true);
        user.SetPreference(PreferenceKind.BlockedTags, ["spoilers"]);
        user.Permissions.Remove(user.Permissions.First(permission => permission.Kind == PermissionKind.EnableAllChannels));

        await _userManager.UpdateUserAsync(user);

        var reloaded = _userManager.GetUserById(user.Id)!;
        Assert.True(reloaded.HasPermission(PermissionKind.IsAdministrator));
        Assert.Equal(new[] { "spoilers" }, reloaded.GetPreference(PreferenceKind.BlockedTags));
        Assert.DoesNotContain(reloaded.Permissions, permission => permission.Kind == PermissionKind.EnableAllChannels);

        await using var context = CreateDbContext();
        Assert.Equal(reloaded.Permissions.Count, await context.Permissions.CountAsync(TestContext.Current.CancellationToken));
    }

    private KhyloDbContext CreateDbContext()
    {
        return new KhyloDbContext(
            _dbOptions,
            NullLogger<KhyloDbContext>.Instance,
            new SqliteDatabaseProvider(null!, NullLogger<SqliteDatabaseProvider>.Instance),
            new NoLockBehavior(NullLogger<NoLockBehavior>.Instance));
    }

    /// <summary>
    /// Reads the identity and concurrency token of every permission and preference row.
    /// </summary>
    private async Task<List<(string Table, int Id, int Kind, uint RowVersion)>> ReadChildRowsAsync()
    {
        await using var context = CreateDbContext();
        var permissions = await context.Permissions
            .OrderBy(permission => permission.Id)
            .Select(permission => new ValueTuple<string, int, int, uint>("Permission", permission.Id, (int)permission.Kind, permission.RowVersion))
            .ToListAsync(TestContext.Current.CancellationToken);
        var preferences = await context.Preferences
            .OrderBy(preference => preference.Id)
            .Select(preference => new ValueTuple<string, int, int, uint>("Preference", preference.Id, (int)preference.Kind, preference.RowVersion))
            .ToListAsync(TestContext.Current.CancellationToken);

        return permissions.Concat(preferences).ToList();
    }

    private sealed class NoopEventManager : IEventManager
    {
        public void Publish<T>(T eventArgs)
            where T : EventArgs
        {
        }

        public Task PublishAsync<T>(T eventArgs)
            where T : EventArgs
            => Task.CompletedTask;
    }
}
