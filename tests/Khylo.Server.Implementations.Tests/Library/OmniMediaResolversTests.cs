#nullable disable

using System.IO;
using Khylo.Controller;
using Khylo.Controller.Entities;
using Khylo.Controller.Library;
using Khylo.Data.Enums;
using Khylo.Model.IO;
using Khylo.Server.Core.Library.Resolvers.AudioBooks;
using Khylo.Server.Core.Library.Resolvers.Comics;
using Khylo.Server.Core.Library.Resolvers.Podcasts;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Khylo.Server.Implementations.Tests.Library
{
    public class OmniMediaResolversTests
    {
        private readonly Mock<IServerApplicationPaths> _appPathsMock = new();
        private readonly Mock<ILibraryManager> _libraryManagerMock = new();

        private ItemResolveArgs CreateArgs(string path, bool isDirectory, CollectionType? collectionType = null, Folder parent = null)
        {
            var args = new ItemResolveArgs(_appPathsMock.Object, _libraryManagerMock.Object)
            {
                FileInfo = new FileSystemMetadata
                {
                    FullName = path,
                    IsDirectory = isDirectory
                },
                CollectionType = collectionType,
                Parent = parent,
                FileSystemChildren = []
            };

            return args;
        }

        [Fact]
        public void ComicResolver_ResolvesCbzFileInComicsCollection()
        {
            var resolver = new ComicResolver();
            var args = CreateArgs(@"C:\Comics\Batman #001 (2016).cbz", false, CollectionType.comics);

            var result = resolver.ResolvePath(args) as Comic;

            Assert.NotNull(result);
            Assert.Equal("Batman #1", result.Name);
            Assert.Equal("Batman", result.SeriesName);
            Assert.Equal(1, result.IndexNumber);
            Assert.Equal("1", result.IssueNumber);
            Assert.Equal(2016, result.ProductionYear);
        }

        [Fact]
        public void ComicResolver_IgnoresNonComicCollectionsWithoutComicParent()
        {
            var resolver = new ComicResolver();
            var args = CreateArgs(@"C:\Movies\Batman #001 (2016).cbz", false, CollectionType.movies);

            var result = resolver.ResolvePath(args);

            Assert.Null(result);
        }

        [Fact]
        public void ComicSeriesResolver_ResolvesDirectoryInComicsCollection()
        {
            var resolver = new ComicSeriesResolver(NullLogger<ComicSeriesResolver>.Instance);
            var args = CreateArgs(@"C:\Comics\Saga (2012)", true, CollectionType.comics);

            var result = resolver.ResolvePath(args) as ComicSeries;

            Assert.NotNull(result);
            Assert.Equal("Saga", result.Name);
            Assert.Equal(2012, result.ProductionYear);
        }

        [Fact]
        public void AudioBookResolver_ResolvesM4bFileInAnyCollection()
        {
            var resolver = new AudioBookResolver();
            var args = CreateArgs(@"C:\Media\Project Hail Mary.m4b", false, null);

            var result = resolver.ResolvePath(args) as AudioBook;

            Assert.NotNull(result);
            Assert.Equal("Project Hail Mary", result.Name);
        }

        [Fact]
        public void AudioBookResolver_ResolvesMp3InAudiobooksCollection()
        {
            var resolver = new AudioBookResolver();
            var args = CreateArgs(@"C:\Audiobooks\Dune Chapter 1.mp3", false, CollectionType.audiobooks);

            var result = resolver.ResolvePath(args) as AudioBook;

            Assert.NotNull(result);
            Assert.Equal("Dune Chapter 1", result.Name);
        }

        [Fact]
        public void PodcastResolver_ResolvesFolderInPodcastsCollection()
        {
            var resolver = new PodcastResolver(NullLogger<PodcastResolver>.Instance);
            var args = CreateArgs(@"C:\Podcasts\The Daily", true, CollectionType.podcasts);

            var result = resolver.ResolvePath(args) as Podcast;

            Assert.NotNull(result);
            Assert.Equal("The Daily", result.Name);
        }

        [Fact]
        public void PodcastEpisodeResolver_ResolvesEpisodeFileWithNumber()
        {
            var podcast = new Podcast { Name = "Tech Talk" };
            var resolver = new PodcastEpisodeResolver();
            var args = CreateArgs(@"C:\Podcasts\Tech Talk\Episode 42 - Future of AI.mp3", false, CollectionType.podcasts, podcast);

            var result = resolver.ResolvePath(args) as PodcastEpisode;

            Assert.NotNull(result);
            Assert.Equal("Episode 42 - Future of AI", result.Name);
            Assert.Equal(42, result.EpisodeNumber);
        }
    }
}
