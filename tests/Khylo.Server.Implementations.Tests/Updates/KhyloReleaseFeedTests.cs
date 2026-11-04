using System.Linq;
using Khylo.Server.Core.Updates;
using Xunit;

namespace Khylo.Server.Implementations.Tests.Updates;

public class KhyloReleaseFeedTests
{
    private const string Feed = """
        [
          { "tag_name": "DEV-V26.11.04.01", "prerelease": false, "draft": false, "body": "dev one", "html_url": "u1", "published_at": "2026-11-04T10:00:00Z",
            "assets": [ { "name": "SHA256SUMS.txt", "browser_download_url": "https://github.com/khylo-org/khylo/releases/download/DEV-V26.11.04.01/SHA256SUMS.txt" } ] },
          { "tag_name": "DEV-V26.11.04.10", "prerelease": false, "draft": false, "body": "dev ten", "html_url": "u2", "published_at": "2026-11-04T12:00:00Z", "assets": [] },
          { "tag_name": "DEV-V26.11.05.01", "prerelease": false, "draft": true, "body": "a draft", "html_url": "u3", "assets": [] },
          { "tag_name": "V26.12.1", "prerelease": false, "draft": false, "body": "stable", "html_url": "u4", "assets": [] },
          { "tag_name": "V27.1.0-rc1", "prerelease": true, "draft": false, "body": "pre", "html_url": "u5", "assets": [] }
        ]
        """;

    [Fact]
    public void Parse_StableChannel_OnlyFinishedReleases()
    {
        var releases = KhyloReleaseFeed.Parse(Feed, "stable");

        Assert.Equal(["V26.12.1"], releases.Select(r => r.Tag));
    }

    [Fact]
    public void Parse_DevChannel_TakesEveryBuildExceptDraftsNewestFirst()
    {
        var releases = KhyloReleaseFeed.Parse(Feed, "dev");

        // A build number of ten is newer than one, not older (compared as numbers, not text)
        Assert.Equal(["V27.1.0-rc1", "V26.12.1", "DEV-V26.11.04.10", "DEV-V26.11.04.01"], releases.Select(r => r.Tag));
    }

    [Fact]
    public void Parse_ReadsAssetsAndDates()
    {
        var release = KhyloReleaseFeed.Parse(Feed, "dev").Single(r => r.Tag == "DEV-V26.11.04.01");

        Assert.Equal("https://github.com/khylo-org/khylo/releases/download/DEV-V26.11.04.01/SHA256SUMS.txt", release.Assets["SHA256SUMS.txt"]);
        Assert.Equal(2026, release.PublishedAt?.Year);
    }

    [Theory]
    [InlineData("DEV-V26.11.04.02", "DEV-V26.11.04.01", true)]
    [InlineData("DEV-V26.11.04.01", "DEV-V26.11.04.01", false)]
    [InlineData("DEV-V26.11.04.01", "DEV-V26.11.04.02", false)]
    [InlineData("DEV-V26.11.05.01", "DEV-V26.11.04.09", true)]
    [InlineData("DEV-V26.12.01.01", "DEV-V26.11.30.09", true)]
    [InlineData("DEV-V26.11.04.10", "DEV-V26.11.04.09", true)]
    public void IsNewer_ComparesBuildNumbersAsNumbers(string candidate, string current, bool expected)
        => Assert.Equal(expected, KhyloReleaseFeed.IsNewer(candidate, current));

    [Fact]
    public void ChecksumFor_FindsTheFileCaseInsensitively()
    {
        const string Sums = "AAAA1111  khylo-server-1-windows-x64-setup.exe\nbbbb2222 *khylo-server-1-linux.tar.gz\n";

        Assert.Equal("aaaa1111", KhyloReleaseFeed.ChecksumFor(Sums, "Khylo-Server-1-Windows-x64-Setup.exe"));
        Assert.Equal("bbbb2222", KhyloReleaseFeed.ChecksumFor(Sums, "khylo-server-1-linux.tar.gz"));
        Assert.Null(KhyloReleaseFeed.ChecksumFor(Sums, "missing.exe"));
    }

    [Theory]
    [InlineData("stable", true)]
    [InlineData("anything", true)]
    [InlineData("dev", false)]
    [InlineData("DEV", false)]
    public void IsStable_OnlyDevIsNotStable(string channel, bool expected)
        => Assert.Equal(expected, KhyloReleaseFeed.IsStable(channel));

    [Fact]
    public void WhatsNew_TakesOnlyThatPartAsPlainText()
    {
        const string Body = """
            **Dev build.** Newer.

            <p align="center">
            <a href="x"><img src="y"></a>
            </p>

            ## What's new in this build

            **New**
            - **Server — Updates itself.** Press [Update now](http://x) and it installs.

            **Fixed**
            - **Web — Backups.** They show progress.

            Full list: [what's new](https://example.com).
            """;

        var lines = KhyloReleaseFeed.WhatsNew(Body).Split((char)10);

        Assert.Equal(["New", "- Server — Updates itself. Press Update now and it installs.", "Fixed", "- Web — Backups. They show progress."], lines);
    }

    [Fact]
    public void WhatsNew_WithoutThatPart_IsEmpty()
        => Assert.Equal(string.Empty, KhyloReleaseFeed.WhatsNew("Just some notes"));
}
