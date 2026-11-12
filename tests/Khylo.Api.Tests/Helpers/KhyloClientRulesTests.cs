using Khylo.Controller.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Khylo.Api.Tests.Helpers;

public class KhyloClientRulesTests
{
    private static HttpRequest Request(string path, string? header = null, string? headerName = "Authorization", string? query = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (header is not null)
        {
            context.Request.Headers[headerName!] = header;
        }

        if (query is not null)
        {
            context.Request.QueryString = new QueryString(query);
        }

        return context.Request;
    }

    // Every app Khylo makes
    [Theory]
    [InlineData("Khylo Web")]
    [InlineData("Khylo TV")]
    [InlineData("Khylo TV (debug)")]
    [InlineData("Khylo iOS")]
    [InlineData("Khylo iPadOS")]
    [InlineData("Khylo Music iOS")]
    [InlineData("Khylo Manager")]
    [InlineData("Khylo")]
    [InlineData("khylo android")]
    public void KhyloApps_AreKhyloClients(string name) => Assert.True(KhyloClientRules.IsKhyloClient(name));

    // Jellyfin apps and anything else
    [Theory]
    [InlineData("Swiftfin iOS")]
    [InlineData("Swiftfin")]
    [InlineData("Jellyfin Web")]
    [InlineData("Jellyfin Android TV")]
    [InlineData("Jellyfin Android")]
    [InlineData("Findroid")]
    [InlineData("Infuse")]
    [InlineData("Streamyfin")]
    [InlineData("Finamp")]
    [InlineData("Not Khylo")]
    [InlineData("MyKhylo")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void OtherApps_AreNotKhyloClients(string? name) => Assert.False(KhyloClientRules.IsKhyloClient(name));

    [Fact]
    public void TheSwiftfinHeader_IsRefused_OnEveryRequest()
    {
        const string swiftfin = "MediaBrowser Client=\"Swiftfin iOS\", Device=\"iPhone\", DeviceId=\"abc\", Version=\"1.4.0\"";
        foreach (var path in new[] { "/System/Info/Public", "/Users/AuthenticateByName", "/Users/Public", "/Items", "/Branding/Configuration", "/Sessions/Playing", "/web/index.html" })
        {
            Assert.True(KhyloClientRules.MustRefuse(Request(path, swiftfin)), path);
        }
    }

    [Fact]
    public void TheKhyloHeader_IsLetThrough()
    {
        const string khylo = "MediaBrowser Client=\"Khylo iOS\", Device=\"iPhone\", DeviceId=\"abc\", Version=\"26.11.11\", Token=\"t\"";
        foreach (var path in new[] { "/System/Info/Public", "/Users/AuthenticateByName", "/Items", "/Sessions/Playing" })
        {
            Assert.False(KhyloClientRules.MustRefuse(Request(path, khylo)), path);
        }
    }

    [Theory]
    [InlineData("Authorization")]
    [InlineData("X-Emby-Authorization")]
    [InlineData("X-MediaBrowser-Authorization")]
    public void TheNameIsFound_InEveryHeaderAnAppMayUse(string header)
    {
        Assert.True(KhyloClientRules.MustRefuse(Request("/Items", "MediaBrowser Client=\"Findroid\", Device=\"Pixel\"", header)));
        Assert.False(KhyloClientRules.MustRefuse(Request("/Items", "MediaBrowser Client=\"Khylo TV\", Device=\"Shield\"", header)));
    }

    [Fact]
    public void TheOlderSeparateHeadersAndTheAddress_AreRead()
    {
        Assert.True(KhyloClientRules.MustRefuse(Request("/Items", "Infuse", "X-Emby-Client")));
        Assert.True(KhyloClientRules.MustRefuse(Request("/Items", query: "?X-Emby-Client=Infuse")));
        Assert.False(KhyloClientRules.MustRefuse(Request("/Items", "Khylo TV", "X-Emby-Client")));
    }

    [Fact]
    public void ANameWithCommasAndEscapesInOtherParts_IsStillRead()
    {
        Assert.True(KhyloClientRules.MustRefuse(Request("/Items", "MediaBrowser Device=\"Bob, the phone\", Client=\"Swiftfin iOS\", Version=\"1\"")));
        Assert.Equal("Khylo Music iOS", KhyloClientRules.ClientFromHeader("MediaBrowser Device=\"a, b\", Client=\"Khylo%20Music%20iOS\""));
    }

    // A Jellyfin app that tells the server nothing about itself must still not be able to sign in
    [Theory]
    [InlineData("/Users/AuthenticateByName")]
    [InlineData("/Users/AuthenticateWithQuickConnect")]
    [InlineData("/QuickConnect/Initiate")]
    [InlineData("/khylo/Users/AuthenticateByName")]
    public void StartingASignIn_WithoutNamingAnApp_IsRefused(string path)
    {
        Assert.True(KhyloClientRules.MustRefuse(Request(path)));
        Assert.True(KhyloClientRules.MustRefuse(Request(path, "MediaBrowser Device=\"x\"")));
    }

    // Requests that name no app and don't start a sign-in must keep working (a picture in a browser, a health check, discovery)
    [Theory]
    [InlineData("/health")]
    [InlineData("/System/Info/Public")]
    [InlineData("/Items/abc/Images/Primary")]
    [InlineData("/web/index.html")]
    public void OrdinaryRequests_NamingNoApp_AreNotRefusedHere(string path) => Assert.False(KhyloClientRules.MustRefuse(Request(path)));
}
