using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Khylo.Api.Auth.SignedInOrHomeNetworkPolicy;
using Khylo.Api.Controllers;
using Khylo.Common.Api;
using Khylo.Common.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Khylo.Api.Tests.Auth;

public class SignedInOrHomeNetworkPolicyTests
{
    private static ClaimsPrincipal SignedIn() => new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "someone")], "test"));

    private static ClaimsPrincipal Nobody() => new(new ClaimsIdentity());

    [Theory]
    [InlineData("SubtitleController", "GetSubtitle")]
    [InlineData("SubtitleController", "GetSubtitleWithTicks")]
    [InlineData("VideoAttachmentsController", "GetAttachment")]
    public void SubtitlesAndAttachments_NeedASignInOrTheHomeNetwork(string controller, string action)
    {
        var type = typeof(SubtitleController).Assembly.GetType("Khylo.Api.Controllers." + controller)!;

        var attribute = type.GetMethod(action)!.GetCustomAttribute<AuthorizeAttribute>();

        Assert.Equal(Policies.SignedInOrHomeNetwork, attribute?.Policy);
    }

    [Fact]
    public async Task AnonymousCallerOnTheHomeNetwork_IsAllowed()
    {
        Assert.True(await Allowed(Nobody(), fromHome: true));
    }

    [Fact]
    public async Task AnonymousCallerFromTheInternet_IsRefused()
    {
        Assert.False(await Allowed(Nobody(), fromHome: false));
    }

    [Fact]
    public async Task SignedInCallerFromTheInternet_IsAllowed()
    {
        Assert.True(await Allowed(SignedIn(), fromHome: false));
    }

    private static async Task<bool> Allowed(ClaimsPrincipal user, bool fromHome)
    {
        var network = new Mock<INetworkManager>();
        network.Setup(n => n.IsInLocalNetwork(It.IsAny<IPAddress>())).Returns(fromHome);
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(fromHome ? "10.0.0.5" : "203.0.113.9");
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns(http);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton<IAuthorizationHandler>(new SignedInOrHomeNetworkHandler(network.Object, accessor.Object));
        services.AddAuthorizationCore(options =>
            options.AddPolicy(Policies.SignedInOrHomeNetwork, policy => policy.Requirements.Add(new SignedInOrHomeNetworkRequirement())));
        var authorization = services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();

        return (await authorization.AuthorizeAsync(user, null, Policies.SignedInOrHomeNetwork)).Succeeded;
    }
}
