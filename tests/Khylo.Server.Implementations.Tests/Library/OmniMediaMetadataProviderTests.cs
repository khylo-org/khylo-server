#nullable disable

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Controller.Entities;
using Khylo.Controller.Providers;
using Khylo.Data.Enums;
using Khylo.Model.Dto;
using Khylo.Model.IO;
using Khylo.Providers.Games;
using Khylo.Providers.Podcasts;
using Khylo.Server.Core.Dto;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Khylo.Server.Implementations.Tests.Library
{
    public class OmniMediaMetadataProviderTests
    {
        [Fact]
        public async Task GameMetadataProvider_EnrichesKnownTitleMetadata()
        {
            var provider = new GameMetadataProvider(NullLogger<GameMetadataProvider>.Instance);
            var directoryServiceMock = new Mock<IDirectoryService>();

            // Create temporary dummy ROM file
            var tempRomFile = Path.Combine(Path.GetTempPath(), "Super Mario 64 (USA) [!].z64");
            await File.WriteAllBytesAsync(tempRomFile, new byte[1024], TestContext.Current.CancellationToken);

            try
            {
                var game = new Game { Path = tempRomFile };
                var info = new ItemInfo(game);
                var result = await provider.GetMetadata(info, directoryServiceMock.Object, TestContext.Current.CancellationToken);

                Assert.True(result.HasMetadata);
                Assert.NotNull(result.Item);
                Assert.Equal("Super Mario 64", result.Item.Name);
                Assert.Equal("Nintendo EAD", result.Item.Developer);
                Assert.Equal("Nintendo", result.Item.Publisher);
                Assert.Equal(1996, result.Item.ProductionYear);
                Assert.Equal(1, result.Item.Players);
                Assert.NotEmpty(result.Item.RomChecksum);
                Assert.Contains("3D Platformer", result.Item.Genres);
            }
            finally
            {
                if (File.Exists(tempRomFile))
                {
                    File.Delete(tempRomFile);
                }
            }
        }

        [Fact]
        public async Task PodcastMetadataProvider_ParsesRssFeedXml()
        {
            var provider = new PodcastMetadataProvider(NullLogger<PodcastMetadataProvider>.Instance);
            var directoryServiceMock = new Mock<IDirectoryService>();

            var tempDir = Path.Combine(Path.GetTempPath(), "KhyloPodcastTest_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var feedXmlContent = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:itunes=""http://www.itunes.com/dtds/podcast-1.0.dtd"">
  <channel>
    <title>The Khylo Tech Podcast</title>
    <link>https://khylo.org</link>
    <description>Deep dive into modern self-hosted media server architecture.</description>
    <itunes:author>Khylo Engineering Team</itunes:author>
    <itunes:category text=""Technology"" />
    <itunes:category text=""Software"" />
  </channel>
</rss>";

            var feedXmlPath = Path.Combine(tempDir, "feed.xml");
            await File.WriteAllTextAsync(feedXmlPath, feedXmlContent, TestContext.Current.CancellationToken);

            try
            {
                var podcast = new Podcast { Path = tempDir };
                var info = new ItemInfo(podcast);
                var result = await provider.GetMetadata(info, directoryServiceMock.Object, TestContext.Current.CancellationToken);

                Assert.True(result.HasMetadata);
                Assert.NotNull(result.Item);
                Assert.Equal("The Khylo Tech Podcast", result.Item.Name);
                Assert.Equal("Deep dive into modern self-hosted media server architecture.", result.Item.Overview);
                Assert.Equal("Khylo Engineering Team", result.Item.Author);
                Assert.Equal("https://khylo.org", result.Item.WebsiteUrl);
                Assert.Contains("Technology", result.Item.Genres);
                Assert.Contains("Software", result.Item.Genres);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public void BaseItemDto_CarriesOmniMediaProperties()
        {
            var dto = new BaseItemDto
            {
                PageCount = 384,
                IssueNumber = "42",
                Volume = 2,
                Publisher = "DC Comics",
                Developer = "Square",
                Platform = "Super Nintendo Entertainment System",
                RomChecksum = "A1B2C3D4",
                Players = 2,
                Narrator = "Ray Porter"
            };

            Assert.Equal(384, dto.PageCount);
            Assert.Equal("42", dto.IssueNumber);
            Assert.Equal(2, dto.Volume);
            Assert.Equal("DC Comics", dto.Publisher);
            Assert.Equal("Square", dto.Developer);
            Assert.Equal("Super Nintendo Entertainment System", dto.Platform);
            Assert.Equal("A1B2C3D4", dto.RomChecksum);
            Assert.Equal(2, dto.Players);
            Assert.Equal("Ray Porter", dto.Narrator);
        }
    }
}
