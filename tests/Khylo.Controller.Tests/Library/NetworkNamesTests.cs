using Khylo.Controller.Library;
using Xunit;

namespace Khylo.Controller.Tests.Library;

public class NetworkNamesTests
{
    [Theory]
    [InlineData(@"\\nas01.\media", true)]
    [InlineData(@"\\nas01.\media\Movies\Action", true)]
    [InlineData(@"\\nas01.example.com\media", false)]
    [InlineData(@"\\nas01\media", false)]
    [InlineData(@"\\192.168.1.50\media", false)]
    [InlineData(@"\\nas01.", true)]
    [InlineData(@"X:\Movies", false)]
    [InlineData(@"/mnt/media", false)]
    [InlineData(@"\\", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TheDotAfterTheServersName_IsRecognised(string? path, bool dotted) => Assert.Equal(dotted, NetworkNames.HasDottedServer(path));

    [Theory]
    [InlineData(@"\\nas01.\media", @"\\nas01\media")]
    [InlineData(@"\\nas01.\media\TV Shows\Scrubs", @"\\nas01\media\TV Shows\Scrubs")]
    [InlineData(@"\\nas01\media\a.b.", @"\\nas01\media\a.b.")]
    [InlineData(@"\\nas01.example.com\media", @"\\nas01.example.com\media")]
    [InlineData(@"X:\Movies.", @"X:\Movies.")]
    public void WithoutTheDot_OnlyTheServersNameChanges(string path, string expected) => Assert.Equal(expected, NetworkNames.WithoutDot(path));

    [Theory]
    [InlineData("nas01.", "nas01")]
    [InlineData("nas01", "nas01")]
    [InlineData("nas.local.", "nas.local")]
    [InlineData("192.168.1.50", "192.168.1.50")]
    public void AServersName_LosesAClosingDot(string host, string expected) => Assert.Equal(expected, NetworkNames.HostWithoutDot(host));

    [Theory]
    [InlineData(@"\\nas01.\media", @"\\nas01\media", true)]
    [InlineData(@"\\NAS01.\MEDIA\", @"\\nas01\media", true)]
    [InlineData(@"\\nas01.\media\Movies", @"\\nas01\media\Movies", true)]
    [InlineData(@"\\nas01\media", @"\\nas01\other", false)]
    [InlineData(@"\\nas01\media", @"\\nas02\media", false)]
    public void TwoSpellingsOfOneShare_AreTheSamePlace(string a, string b, bool same) => Assert.Equal(same, NetworkNames.SameShare(a, b));

    [Fact]
    public void ADottedPath_GetsAWarningNamingThePlainSpelling()
    {
        var warning = NetworkNames.Warning(@"\\nas01.\media\Movies");
        Assert.NotNull(warning);
        Assert.Contains(@"\\nas01.\media\Movies", warning, System.StringComparison.Ordinal);
        Assert.Contains(@"\\nas01\media\Movies", warning, System.StringComparison.Ordinal);
        Assert.Contains("restarts", warning, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"\\nas01\media")]
    [InlineData(@"D:\Media")]
    [InlineData(null)]
    public void AnythingElse_GetsNoWarning(string? path) => Assert.Null(NetworkNames.Warning(path));
}
