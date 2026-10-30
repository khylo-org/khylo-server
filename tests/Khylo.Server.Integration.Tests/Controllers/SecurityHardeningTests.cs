using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Khylo.Api.Models.UserDtos;
using Khylo.Controller.Library;
using Khylo.Controller.Net;
using Khylo.Data.Enums;
using Khylo.Extensions.Json;
using Khylo.Model.Dto;
using Khylo.Model.Invitations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

#pragma warning disable CS0618 // the old SystemInfo path fields are exactly what is being checked

namespace Khylo.Server.Integration.Tests.Controllers;

/// <summary>
/// The security fixes: what an ordinary account can and can't see or do, and the headers every response carries.
/// Runs against an in-memory server with its own temporary folders.
/// </summary>
public sealed class SecurityHardeningTests : IClassFixture<KhyloApplicationFactory>
{
    private const string Password = "a-long-enough-password";

    // The first-run wizard can only be completed once per server, so every test shares one set-up
    private static Task<(HttpClient Admin, HttpClient Alice, Guid AliceId, Guid BobId)>? _setUp;

    private readonly KhyloApplicationFactory _factory;
    private readonly JsonSerializerOptions _json = JsonDefaults.Options;

    public SecurityHardeningTests(KhyloApplicationFactory factory)
    {
        _factory = factory;
    }

    private Task<(HttpClient Admin, HttpClient Alice, Guid AliceId, Guid BobId)> SetUpAsync()
        => _setUp ??= SetUpCoreAsync();

    private async Task<(HttpClient Admin, HttpClient Alice, Guid AliceId, Guid BobId)> SetUpCoreAsync()
    {
        var admin = _factory.CreateClient();
        admin.DefaultRequestHeaders.AddAuthHeader(await AuthHelper.CompleteStartupAsync(admin));

        // Created through the API, or (if an earlier test in this server already made them) looked up
        var all = await admin.GetFromJsonAsync<UserDto[]>("Users", _json, TestContext.Current.CancellationToken);
        foreach (var name in new[] { "alice", "bob" })
        {
            if (!all!.Any(u => u.Name == name))
            {
                using var created = await admin.PostAsJsonAsync("Users/New", new CreateUserByName { Name = name, Password = Password }, _json, TestContext.Current.CancellationToken);
                created.EnsureSuccessStatusCode();
            }
        }

        all = await admin.GetFromJsonAsync<UserDto[]>("Users", _json, TestContext.Current.CancellationToken);
        var alice = all!.First(u => u.Name == "alice").Id;
        var bob = all!.First(u => u.Name == "bob").Id;

        var aliceClient = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "Users/AuthenticateByName");
        request.Headers.TryAddWithoutValidation(AuthHelper.AuthHeaderName, AuthHelper.DummyAuthHeader);
        request.Content = JsonContent.Create(new AuthenticateUserByName { Username = "alice", Pw = Password }, options: _json);
        using var auth = await aliceClient.SendAsync(request, TestContext.Current.CancellationToken);
        auth.EnsureSuccessStatusCode();
        var result = await auth.Content.ReadFromJsonAsync<AuthResult>(_json, TestContext.Current.CancellationToken);
        aliceClient.DefaultRequestHeaders.AddAuthHeader(result!.AccessToken);
        return (admin, aliceClient, alice, bob);
    }

    [Fact]
    public async Task Responses_CarryBrowserSecurityHeaders()
    {
        var client = _factory.CreateClient();
        using var response = await client.GetAsync("System/Info/Public", TestContext.Current.CancellationToken);

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("same-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("SAMEORIGIN", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'self'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Restart_AsOrdinaryUser_IsForbidden_EvenFromTheLocalMachine()
    {
        var (_, alice, _, _) = await SetUpAsync();

        using var response = await alice.PostAsync("System/Restart", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UserList_OrdinaryUserSeesOnlyThemselves_AdminSeesEveryone()
    {
        var (admin, alice, aliceId, bobId) = await SetUpAsync();

        var seenByAlice = await alice.GetFromJsonAsync<UserDto[]>("Users", _json, TestContext.Current.CancellationToken);
        var seenByAdmin = await admin.GetFromJsonAsync<UserDto[]>("Users", _json, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { aliceId }, seenByAlice!.Select(u => u.Id));
        Assert.Contains(bobId, seenByAdmin!.Select(u => u.Id));
        Assert.True(seenByAdmin!.Length >= 3);
    }

    [Fact]
    public async Task UserById_OrdinaryUserCanReadSelfButNotSomeoneElse()
    {
        var (_, alice, aliceId, bobId) = await SetUpAsync();

        using var self = await alice.GetAsync("Users/" + aliceId.ToString("N", CultureInfo.InvariantCulture), TestContext.Current.CancellationToken);
        using var other = await alice.GetAsync("Users/" + bobId.ToString("N", CultureInfo.InvariantCulture), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, self.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
    }

    [Fact]
    public async Task SystemInfo_HidesFolderPathsFromOrdinaryUsers_ButNotAdmins()
    {
        var (admin, alice, _, _) = await SetUpAsync();

        var forAlice = await alice.GetFromJsonAsync<Khylo.Model.System.SystemInfo>("System/Info", _json, TestContext.Current.CancellationToken);
        var forAdmin = await admin.GetFromJsonAsync<Khylo.Model.System.SystemInfo>("System/Info", _json, TestContext.Current.CancellationToken);

        Assert.True(string.IsNullOrEmpty(forAlice!.ProgramDataPath));
        Assert.True(string.IsNullOrEmpty(forAlice.LogPath));
        Assert.True(string.IsNullOrEmpty(forAlice.CachePath));
        Assert.False(string.IsNullOrEmpty(forAdmin!.ProgramDataPath));
    }

    [Fact]
    public async Task SigningInWithoutAPassword_IsRefused_ForADisabledAccount()
    {
        var (admin, _, _, bobId) = await SetUpAsync();
        var users = _factory.Services.GetRequiredService<IUserManager>();
        var bob = users.GetUserById(bobId)!;

        // Allowed while the account is enabled
        users.EnsureCanSignInWithoutPassword(bob, "127.0.0.1");

        var policy = users.GetUserDto(bob).Policy!;
        policy.IsDisabled = true;
        await users.UpdatePolicyAsync(bobId, policy);

        Assert.Throws<SecurityException>(() => users.EnsureCanSignInWithoutPassword(users.GetUserById(bobId)!, "127.0.0.1"));
        Assert.NotNull(admin);
    }

    [Fact]
    public async Task InvitationSignUp_RefusesShortPasswords_AndCreatesNoAccount()
    {
        var (admin, _, _, _) = await SetUpAsync();
        using var created = await admin.PostAsJsonAsync("Invitations", new CreateInvitationRequest { LifetimeHours = 1, MaxUses = 1 }, _json, TestContext.Current.CancellationToken);
        created.EnsureSuccessStatusCode();
        var invitation = await created.Content.ReadFromJsonAsync<KhyloInvitationDto>(_json, TestContext.Current.CancellationToken);

        using var anonymous = _factory.CreateClient();
        using var response = await anonymous.PostAsJsonAsync(
            "Invitations/" + invitation!.Code + "/Redeem",
            new { Username = "shortpw", Password = "1234567" },
            _json,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("8 characters", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        var all = await admin.GetFromJsonAsync<UserDto[]>("Users", _json, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(all!, u => u.Name == "shortpw");
    }

    private sealed class AuthResult
    {
        public string AccessToken { get; set; } = string.Empty;
    }
}
