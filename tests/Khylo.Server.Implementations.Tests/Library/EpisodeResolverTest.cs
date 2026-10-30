using Khylo.Controller;
using Khylo.Controller.Entities;
using Khylo.Controller.Entities.TV;
using Khylo.Controller.Library;
using Khylo.Controller.Providers;
using Khylo.Data.Enums;
using Khylo.Model.Entities;
using Khylo.Model.IO;
using Khylo.Naming.Common;
using Khylo.Server.Core.Library.Resolvers.TV;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Khylo.Server.Implementations.Tests.Library
{
    public class EpisodeResolverTest
    {
        private static readonly NamingOptions _namingOptions = new();

        [Fact]
        public void Resolve_GivenVideoInExtrasFolder_DoesNotResolveToEpisode()
        {
            var parent = new Folder { Name = "extras" };

            var episodeResolver = new EpisodeResolver(Mock.Of<ILogger<EpisodeResolver>>(), _namingOptions, Mock.Of<IDirectoryService>());
            var itemResolveArgs = new ItemResolveArgs(
                Mock.Of<IServerApplicationPaths>(),
                null)
            {
                Parent = parent,
                CollectionType = CollectionType.tvshows,
                FileInfo = new FileSystemMetadata
                {
                    FullName = "All My Children/Season 01/Extras/All My Children S01E01 - Behind The Scenes.mkv"
                }
            };

            Assert.Null(episodeResolver.Resolve(itemResolveArgs));
        }

        [Fact]
        public void Resolve_GivenVideoInExtrasSeriesFolder_ResolvesToEpisode()
        {
            var series = new Series { Name = "Extras" };

            // Have to create a mock because of moq proxies not being castable to a concrete implementation
            // an upstream report
            var episodeResolver = new EpisodeResolverMock(Mock.Of<ILogger<EpisodeResolver>>(), _namingOptions, Mock.Of<IDirectoryService>());
            var itemResolveArgs = new ItemResolveArgs(
                Mock.Of<IServerApplicationPaths>(),
                null)
            {
                Parent = series,
                CollectionType = CollectionType.tvshows,
                FileInfo = new FileSystemMetadata
                {
                    FullName = "Extras/Extras S01E01.mkv"
                }
            };
            Assert.NotNull(episodeResolver.Resolve(itemResolveArgs));
        }

        [Theory]
        [InlineData("/media/Family Guy/DVD Extras Vol. 1 - Vol. 7/Vol. 6 Extras/title25.mp4", "/media/Family Guy", true)]
        [InlineData("/media/Show/Season 01/Featurettes/Making Of.mkv", "/media/Show", true)]
        [InlineData("/media/Show/Bonus Features/Gag Reel.mkv", "/media/Show", true)]
        [InlineData("/media/Show/Behind the Scenes/Part 1.mkv", "/media/Show", true)]
        [InlineData("/media/Extras/Season 1/Extras S01E01.mkv", "/media/Extras", false)]
        [InlineData("/media/Bonus Family/Season 1/Bonus Family S01E01.mkv", "/media/Bonus Family", false)]
        [InlineData("/media/Brothers & Sisters/Season 1/S01E01.mkv", "/media/Brothers & Sisters", false)]
        [InlineData("/media/Show/Extraordinary Season/S01E01.mkv", "/media/Show", false)]
        [InlineData("/media/Show/Season 01/Show S01E01.mkv", null, false)]
        public void IsInsideExtrasFolder_OnlyMatchesBonusFoldersBelowSeries(string path, string? seriesPath, bool expected)
        {
            Assert.Equal(expected, EpisodeResolver.IsInsideExtrasFolder(path, seriesPath));
        }

        [Theory]
        [InlineData("/media/Show/Season 01/Show S01E01 [tvdbid=12345].mkv", MetadataProvider.Tvdb, "12345")]
        [InlineData("/media/Show/Season 01/Show S01E01 [tvdbid-12345].mkv", MetadataProvider.Tvdb, "12345")]
        [InlineData("/media/Show/Season 01/Show S01E01 (tvdbid=12345).mkv", MetadataProvider.Tvdb, "12345")]
        [InlineData("/media/Show/Season 02/Show S02E03 [tvmazeid=67890].mkv", MetadataProvider.TvMaze, "67890")]
        [InlineData("/media/Show/Season 02/Show S02E03 [tvmazeid-67890].mkv", MetadataProvider.TvMaze, "67890")]
        [InlineData("/media/Show/Season 03/Show S03E04 [tmdbid=99999].mkv", MetadataProvider.Tmdb, "99999")]
        [InlineData("/media/Show/Season 03/Show S03E04 [tmdbid-99999].mkv", MetadataProvider.Tmdb, "99999")]
        [InlineData("/media/Show/Season 04/Show S04E05 [imdbid=tt1234567].mkv", MetadataProvider.Imdb, "tt1234567")]
        [InlineData("/media/Show/Season 04/Show S04E05 [imdbid-tt1234567].mkv", MetadataProvider.Imdb, "tt1234567")]
        public void Resolve_EpisodeFileWithProviderId_SetsProviderId(string path, MetadataProvider provider, string expectedId)
        {
            var series = new Series { Name = "Show" };
            var episodeResolver = new EpisodeResolverMock(Mock.Of<ILogger<EpisodeResolver>>(), _namingOptions, Mock.Of<IDirectoryService>());
            var itemResolveArgs = new ItemResolveArgs(
                Mock.Of<IServerApplicationPaths>(),
                null)
            {
                Parent = series,
                CollectionType = CollectionType.tvshows,
                FileInfo = new FileSystemMetadata
                {
                    FullName = path,
                    IsDirectory = false
                }
            };

            var episode = episodeResolver.Resolve(itemResolveArgs);

            Assert.NotNull(episode);
            Assert.True(episode.TryGetProviderId(provider, out var actualId));
            Assert.Equal(expectedId, actualId);
        }

        [Fact]
        public void Resolve_EpisodeFileWithProviderIdsOnAllLevels_OnlyUsesEpisodeLevelId()
        {
            // Series folder has tvdbid=11111, season folder has tvdbid=22222, episode file has tvdbid=33333.
            // The episode should only pick up its own ID, not the series- or season-level ones.
            var series = new Series { Name = "Show" };
            var episodeResolver = new EpisodeResolverMock(Mock.Of<ILogger<EpisodeResolver>>(), _namingOptions, Mock.Of<IDirectoryService>());
            var itemResolveArgs = new ItemResolveArgs(
                Mock.Of<IServerApplicationPaths>(),
                null)
            {
                Parent = series,
                CollectionType = CollectionType.tvshows,
                FileInfo = new FileSystemMetadata
                {
                    FullName = "/media/Show [tvdbid=11111]/Season 01 [tvdbid=22222]/Show S01E01 [tvdbid=33333].mkv",
                    IsDirectory = false
                }
            };

            var episode = episodeResolver.Resolve(itemResolveArgs);

            Assert.NotNull(episode);
            Assert.True(episode.TryGetProviderId(MetadataProvider.Tvdb, out var tvdbId));
            Assert.Equal("33333", tvdbId);
        }

        [Fact]
        public void Resolve_EpisodeFileWithMultipleProviderIds_SetsAll()
        {
            var series = new Series { Name = "Show" };
            var episodeResolver = new EpisodeResolverMock(Mock.Of<ILogger<EpisodeResolver>>(), _namingOptions, Mock.Of<IDirectoryService>());
            var itemResolveArgs = new ItemResolveArgs(
                Mock.Of<IServerApplicationPaths>(),
                null)
            {
                Parent = series,
                CollectionType = CollectionType.tvshows,
                FileInfo = new FileSystemMetadata
                {
                    FullName = "/media/Show/Season 01/Show S01E01 [tvdbid=12345][tmdbid=99999].mkv",
                    IsDirectory = false
                }
            };

            var episode = episodeResolver.Resolve(itemResolveArgs);

            Assert.NotNull(episode);
            Assert.True(episode.TryGetProviderId(MetadataProvider.Tvdb, out var tvdbId));
            Assert.Equal("12345", tvdbId);
            Assert.True(episode.TryGetProviderId(MetadataProvider.Tmdb, out var tmdbId));
            Assert.Equal("99999", tmdbId);
        }

        private sealed class EpisodeResolverMock : EpisodeResolver
        {
            public EpisodeResolverMock(ILogger<EpisodeResolver> logger, NamingOptions namingOptions, IDirectoryService directoryService) : base(logger, namingOptions, directoryService)
            {
            }

            protected override TVideoType ResolveVideo<TVideoType>(ItemResolveArgs args, bool parseName) => new();
        }
    }
}
