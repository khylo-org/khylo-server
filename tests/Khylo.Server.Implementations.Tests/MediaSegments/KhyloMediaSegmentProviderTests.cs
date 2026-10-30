#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Common.Configuration;
using Khylo.Controller.Chapters;
using Khylo.Controller.Configuration;
using Khylo.Controller.Entities.TV;
using Khylo.Controller.Library;
using Khylo.Controller.MediaEncoding;
using Khylo.Database.Implementations.Enums;
using Khylo.Model;
using Khylo.Model.Configuration;
using Khylo.Model.Entities;
using Khylo.Model.MediaSegments;
using Khylo.Server.Implementations.MediaSegments;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Khylo.Server.Implementations.Tests.MediaSegments
{
    public class KhyloMediaSegmentProviderTests
    {
        // A detector with no stored results, so only named chapters count
        private static KhyloSegmentDetector EmptyDetector()
        {
            var paths = new Mock<IApplicationPaths>();
            paths.Setup(p => p.DataPath).Returns(Path.Combine(Path.GetTempPath(), "khylo-segment-tests", Guid.NewGuid().ToString("N")));
            return new KhyloSegmentDetector(NullLogger<KhyloSegmentDetector>.Instance, Mock.Of<IMediaEncoder>(), paths.Object);
        }

        [Fact]
        public async Task GetMediaSegments_ExtractsNamedChapters_IntroAndOutro()
        {
            var libraryManagerMock = new Mock<ILibraryManager>();
            var chapterManagerMock = new Mock<IChapterManager>();
            var configManagerMock = new Mock<IServerConfigurationManager>();

            var episodeId = Guid.NewGuid();
            var episode = new Episode
            {
                Id = episodeId,
                Name = "Pilot",
                RunTimeTicks = TimeSpan.FromMinutes(22).Ticks
            };

            var chapters = new List<ChapterInfo>
            {
                new ChapterInfo { Name = "Prologue", StartPositionTicks = 0 },
                new ChapterInfo { Name = "Main Theme Opening", StartPositionTicks = TimeSpan.FromMinutes(2).Ticks },
                new ChapterInfo { Name = "Act 1", StartPositionTicks = TimeSpan.FromMinutes(3.5).Ticks },
                new ChapterInfo { Name = "End Credits", StartPositionTicks = TimeSpan.FromMinutes(20.5).Ticks }
            };

            libraryManagerMock.Setup(l => l.GetItemById(episodeId)).Returns(episode);
            chapterManagerMock.Setup(c => c.GetChapters(episodeId)).Returns(chapters);
            configManagerMock.Setup(c => c.Configuration).Returns(new ServerConfiguration
            {
                EnableIntroDetection = true,
                EnableCreditsDetection = true
            });

            var provider = new KhyloMediaSegmentProvider(
                NullLogger<KhyloMediaSegmentProvider>.Instance,
                libraryManagerMock.Object,
                chapterManagerMock.Object,
                configManagerMock.Object,
                EmptyDetector());

            var request = new MediaSegmentGenerationRequest
            {
                ItemId = episodeId,
                ExistingSegments = Array.Empty<MediaSegmentDto>()
            };

            var segments = await provider.GetMediaSegments(request, TestContext.Current.CancellationToken);

            Assert.NotNull(segments);
            Assert.Contains(segments, s => s.Type == MediaSegmentType.Intro && s.StartTicks == TimeSpan.FromMinutes(2).Ticks);
            Assert.Contains(segments, s => s.Type == MediaSegmentType.Outro && s.StartTicks == TimeSpan.FromMinutes(20.5).Ticks);
        }

        [Fact]
        public async Task GetMediaSegments_GenericChapterNames_ProducesNoGuessedSegments()
        {
            var segments = await GetSegments(
                TimeSpan.FromMinutes(24),
                ("Chapter 1", TimeSpan.Zero),
                ("Chapter 2", TimeSpan.FromSeconds(90)),
                ("Chapter 3", TimeSpan.FromSeconds(180)),
                ("Chapter 4", TimeSpan.FromMinutes(22.5)));

            Assert.Empty(segments);
        }

        [Fact]
        public async Task GetMediaSegments_IntroStartEndPair_IntroEndsAtEndMarker()
        {
            var segments = await GetSegments(
                TimeSpan.FromMinutes(22),
                ("Scene 1", TimeSpan.Zero),
                ("Intro start", TimeSpan.FromSeconds(60)),
                ("Intro end", TimeSpan.FromSeconds(120)),
                ("Scene 2", TimeSpan.FromMinutes(5)));

            var intro = Assert.Single(segments);
            Assert.Equal(MediaSegmentType.Intro, intro.Type);
            Assert.Equal(TimeSpan.FromSeconds(60).Ticks, intro.StartTicks);
            Assert.Equal(TimeSpan.FromSeconds(120).Ticks, intro.EndTicks);
        }

        [Fact]
        public async Task GetMediaSegments_ImplausiblyPlacedOrSizedChapters_AreIgnored()
        {
            var segments = await GetSegments(
                TimeSpan.FromMinutes(100),
                // 10 minute "Opening Credits" chapter: story plays under these credits, skipping would lose it.
                ("Opening Credits", TimeSpan.Zero),
                ("Main", TimeSpan.FromMinutes(10)),
                // "Credits" in the first half of the runtime is not end credits.
                ("Credits", TimeSpan.FromMinutes(30)),
                ("Main 2", TimeSpan.FromMinutes(31)),
                ("End Credits", TimeSpan.FromMinutes(94)));

            var outro = Assert.Single(segments);
            Assert.Equal(MediaSegmentType.Outro, outro.Type);
            Assert.Equal(TimeSpan.FromMinutes(94).Ticks, outro.StartTicks);
            Assert.Equal(TimeSpan.FromMinutes(100).Ticks, outro.EndTicks);
        }

        private static async Task<IReadOnlyList<MediaSegmentDto>> GetSegments(TimeSpan runtime, params (string Name, TimeSpan Start)[] chapterMarkers)
        {
            var libraryManagerMock = new Mock<ILibraryManager>();
            var chapterManagerMock = new Mock<IChapterManager>();
            var configManagerMock = new Mock<IServerConfigurationManager>();

            var episodeId = Guid.NewGuid();
            var episode = new Episode
            {
                Id = episodeId,
                Name = "Episode",
                RunTimeTicks = runtime.Ticks
            };

            var chapters = new List<ChapterInfo>();
            foreach (var (name, start) in chapterMarkers)
            {
                chapters.Add(new ChapterInfo { Name = name, StartPositionTicks = start.Ticks });
            }

            libraryManagerMock.Setup(l => l.GetItemById(episodeId)).Returns(episode);
            chapterManagerMock.Setup(c => c.GetChapters(episodeId)).Returns(chapters);
            configManagerMock.Setup(c => c.Configuration).Returns(new ServerConfiguration
            {
                EnableIntroDetection = true,
                EnableCreditsDetection = true
            });

            var provider = new KhyloMediaSegmentProvider(
                NullLogger<KhyloMediaSegmentProvider>.Instance,
                libraryManagerMock.Object,
                chapterManagerMock.Object,
                configManagerMock.Object,
                EmptyDetector());

            return await provider.GetMediaSegments(
                new MediaSegmentGenerationRequest { ItemId = episodeId, ExistingSegments = Array.Empty<MediaSegmentDto>() },
                TestContext.Current.CancellationToken);
        }

        [Fact]
        public void UserConfiguration_DefaultSkipModes_Configured()
        {
            var userConfig = new UserConfiguration();
            Assert.Equal(PlaybackSkipMode.ShowButton, userConfig.SkipIntroMode);
            Assert.Equal(PlaybackSkipMode.ShowButton, userConfig.SkipCreditsMode);
        }
    }
}
