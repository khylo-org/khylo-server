using System.Threading.Tasks;
using Khylo.Controller.Configuration;
using Khylo.Model.Configuration;
using Khylo.Server.Configuration;
using Khylo.Server.Middleware;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace Khylo.Server.Tests;

public class CorsAndHeadersTests
{
    [Fact]
    public void TheFactorySetting_DoesNotAllowEveryWebsite()
    {
        var config = new ServerConfiguration { CorsHosts = ["*"], KhyloWebAddress = "khylo.example.com", KhyloHomeAddress = "http://10.0.0.5:8096/" };

        var origins = CorsPolicyProvider.AllowedOrigins(config);

        Assert.Equal(["https://khylo.example.com", "http://10.0.0.5:8096"], origins);
    }

    [Fact]
    public void ListedHosts_AreAllowed_AndNothingElse()
    {
        var config = new ServerConfiguration { CorsHosts = ["https://receiver.example.net/", "not a url", "*"] };

        Assert.Equal(["https://receiver.example.net"], CorsPolicyProvider.AllowedOrigins(config));
    }

    [Fact]
    public async Task APageFromAnotherWebsite_IsNotAllowed_TheServersOwnAddressIs()
    {
        var configuration = new ServerConfiguration { CorsHosts = ["*"], KhyloWebAddress = "https://khylo.example.com" };
        var manager = new Mock<IServerConfigurationManager>();
        manager.SetupGet(m => m.Configuration).Returns(configuration);
        var provider = new CorsPolicyProvider(manager.Object);

        var policy = await provider.GetPolicyAsync(new DefaultHttpContext(), null);

        Assert.NotNull(policy);
        Assert.False(policy.AllowAnyOrigin);
        Assert.False(policy.IsOriginAllowed("https://evil.example"));
        Assert.False(policy.IsOriginAllowed("http://khylo.example.com"));
        Assert.True(policy.IsOriginAllowed("https://khylo.example.com"));
        Assert.Contains("https://khylo.example.com", policy.Origins);
        Assert.DoesNotContain("*", policy.Origins);
    }

    [Fact]
    public async Task WithNothingConfigured_NoWebsiteIsAllowed()
    {
        var manager = new Mock<IServerConfigurationManager>();
        manager.SetupGet(m => m.Configuration).Returns(new ServerConfiguration { CorsHosts = [] });

        var policy = await new CorsPolicyProvider(manager.Object).GetPolicyAsync(new DefaultHttpContext(), null);

        Assert.NotNull(policy);
        Assert.False(policy.AllowAnyOrigin);
        Assert.Empty(policy.Origins);
        Assert.False(policy.IsOriginAllowed("https://evil.example"));
    }

    [Theory]
    [InlineData(true, "khylo.example.com", true)]
    [InlineData(true, "Khylo.Example.COM.", true)]
    [InlineData(false, "khylo.example.com", false)]
    [InlineData(true, "10.0.0.5", false)]
    [InlineData(true, "[::1]", false)]
    [InlineData(true, "localhost", false)]
    [InlineData(true, "khylo", false)]
    [InlineData(true, "khylo.local", false)]
    [InlineData(true, "nas.home.arpa", false)]
    [InlineData(true, "", false)]
    [InlineData(true, null, false)]
    public void StrictTransportSecurity_OnlyForARealNameOverHttps(bool https, string? host, bool expected)
    {
        Assert.Equal(expected, SecurityHeadersMiddleware.ShouldSendHsts(https, host));
    }
}
