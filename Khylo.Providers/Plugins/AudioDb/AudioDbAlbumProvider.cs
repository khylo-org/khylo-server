#nullable disable

#pragma warning disable CA1002, CS1591, SA1300

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Common.Configuration;
using Khylo.Common.Extensions;
using Khylo.Common.Net;
using Khylo.Controller.Configuration;
using Khylo.Controller.Entities.Audio;
using Khylo.Controller.Library;
using Khylo.Controller.Providers;
using Khylo.Extensions.Json;
using Khylo.Model.Entities;
using Khylo.Model.IO;
using Khylo.Model.Providers;
using Khylo.Providers.Manager;
using Khylo.Providers.Music;
using Khylo.Providers.Music.Online;

namespace Khylo.Providers.Plugins.AudioDb
{
    public class AudioDbAlbumProvider : IRemoteMetadataProvider<MusicAlbum, AlbumInfo>, IHasOrder
    {
        private readonly IServerConfigurationManager _config;
        private readonly IFileSystem _fileSystem;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly JsonSerializerOptions _jsonOptions = JsonDefaults.Options;

#pragma warning disable SA1401, CA2211
        public static AudioDbAlbumProvider Current;
#pragma warning restore SA1401, CA2211

        public AudioDbAlbumProvider(IServerConfigurationManager config, IFileSystem fileSystem, IHttpClientFactory httpClientFactory)
        {
            _config = config;
            _fileSystem = fileSystem;
            _httpClientFactory = httpClientFactory;

            Current = this;
        }

        /// <inheritdoc />
        public string Name => "TheAudioDB";

        /// <inheritdoc />
        // Khylo: first, it's US-hosted and can match by name; MusicBrainz is a fallback
        public int Order => 0;

        /// <inheritdoc />
        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(AlbumInfo searchInfo, CancellationToken cancellationToken)
            => Task.FromResult(Enumerable.Empty<RemoteSearchResult>());

        /// <inheritdoc />
        public async Task<MetadataResult<MusicAlbum>> GetMetadata(AlbumInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<MusicAlbum>();
            var album = await GetAlbum(info.GetReleaseGroupId(), info.AlbumArtists.Count > 0 ? info.AlbumArtists[0] : null, info.Name, cancellationToken).ConfigureAwait(false);

            if (album is not null)
            {
                result.Item = new MusicAlbum();
                result.HasMetadata = true;
                ProcessResult(result, album, info.MetadataLanguage);
            }

            return result;
        }

        /// <summary>
        /// Resolves the cached AudioDB album: by MusicBrainz release group, then by artist and album name
        /// (for files without ids, like Apple Music downloads).
        /// </summary>
        /// <param name="releaseGroupId">The MusicBrainz release group id, if known.</param>
        /// <param name="artist">The album artist.</param>
        /// <param name="albumName">The album's title.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The matching album, or <c>null</c>.</returns>
        internal async Task<Album> GetAlbum(string releaseGroupId, string artist, string albumName, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(releaseGroupId))
            {
                await EnsureInfo(releaseGroupId, cancellationToken).ConfigureAwait(false);
                var byId = await ReadAlbums(GetAlbumInfoPath(_config.ApplicationPaths, releaseGroupId), cancellationToken).ConfigureAwait(false);
                if (byId.Count > 0)
                {
                    return byId[0];
                }
            }

            if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(albumName))
            {
                return null;
            }

            var path = GetAlbumInfoPath(_config.ApplicationPaths, MusicNameMatch.CacheKey(artist, albumName));
            var fileInfo = _fileSystem.GetFileSystemInfo(path);
            if (!fileInfo.Exists || (DateTime.UtcNow - _fileSystem.GetLastWriteTimeUtc(fileInfo)).TotalDays > 30)
            {
                var url = AudioDbArtistProvider.BaseUrl + "/searchalbum.php?s=" + Uri.EscapeDataString(artist) + "&a=" + Uri.EscapeDataString(albumName);
                await Download(url, path, cancellationToken).ConfigureAwait(false);
            }

            var byName = await ReadAlbums(path, cancellationToken).ConfigureAwait(false);
            return byName.FirstOrDefault(a => MusicNameMatch.SameArtist(a.strArtist, artist) && MusicNameMatch.SameAlbum(a.strAlbum, albumName));
        }

        private async Task<List<Album>> ReadAlbums(string path, CancellationToken cancellationToken)
        {
            if (!File.Exists(path))
            {
                return [];
            }

            FileStream jsonStream = AsyncFile.OpenRead(path);
            await using (jsonStream.ConfigureAwait(false))
            {
                try
                {
                    var obj = await JsonSerializer.DeserializeAsync<RootObject>(jsonStream, _jsonOptions, cancellationToken).ConfigureAwait(false);
                    return obj?.album ?? [];
                }
                catch (JsonException)
                {
                    return [];
                }
            }
        }

        private async Task Download(string url, string path, CancellationToken cancellationToken)
        {
            // Khylo: never during library scans, and within TheAudioDB's request limits
            if (!MusicEnrichment.MayGoOnline || !await SourceRateLimit.WaitTurnAsync(SourceRateLimit.TheAudioDb, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path));

            using var response = await _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (SourceRateLimit.CheckForPause(SourceRateLimit.TheAudioDb, response))
            {
                return;
            }

            response.EnsureSuccessStatusCode();
            var fileStreamOptions = AsyncFile.WriteOptions;
            fileStreamOptions.Mode = FileMode.Create;
            var fs = new FileStream(path, fileStreamOptions);
            await using (fs.ConfigureAwait(false))
            {
                await response.Content.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);
            }
        }

        private void ProcessResult(MetadataResult<MusicAlbum> metadataResult, Album result, string preferredLanguage)
        {
            var item = metadataResult.Item;

            if (Plugin.Instance.Configuration.ReplaceAlbumName && !string.IsNullOrWhiteSpace(result.strAlbum))
            {
                item.Album = result.strAlbum;
            }

            if (!string.IsNullOrWhiteSpace(result.strArtist))
            {
                item.AlbumArtists = [result.strArtist];
            }

            if (!string.IsNullOrEmpty(result.intYearReleased))
            {
                item.ProductionYear = int.Parse(result.intYearReleased, CultureInfo.InvariantCulture);
            }

            if (!string.IsNullOrEmpty(result.strGenre))
            {
                item.Genres = [result.strGenre];
            }

            item.SetProviderId(MetadataProvider.AudioDbArtist, result.idArtist);
            item.SetProviderId(MetadataProvider.AudioDbAlbum, result.idAlbum);

            item.SetProviderId(MetadataProvider.MusicBrainzAlbumArtist, result.strMusicBrainzArtistID);
            item.SetProviderId(MetadataProvider.MusicBrainzReleaseGroup, result.strMusicBrainzID);

            var language = MetadataLanguageUtils.GetLanguageSubtag(preferredLanguage);
            var overview = GetDescription(result, language);

            if (string.IsNullOrWhiteSpace(overview))
            {
                overview = string.IsNullOrWhiteSpace(result.strDescriptionEN)
                    ? result.strDescription
                    : result.strDescriptionEN;

                // The description is not in the requested language, mark it as English so it does not
                // block a provider further down the list that can serve the requested language
                metadataResult.ResultLanguage = "en";
            }
            else
            {
                metadataResult.ResultLanguage = language;
            }

            item.Overview = (overview ?? string.Empty).StripHtml();
        }

        private static string GetDescription(Album result, string language)
            => language switch
            {
                "de" => result.strDescriptionDE,
                "en" => result.strDescriptionEN,
                "es" => result.strDescriptionES,
                "fr" => result.strDescriptionFR,
                "he" => result.strDescriptionIL,
                "hu" => result.strDescriptionHU,
                "it" => result.strDescriptionIT,
                "ja" => result.strDescriptionJP,
                "nl" => result.strDescriptionNL,
                "no" or "nb" or "nn" => result.strDescriptionNO,
                "pl" => result.strDescriptionPL,
                "pt" => result.strDescriptionPT,
                "ru" => result.strDescriptionRU,
                "sv" => result.strDescriptionSE,
                "zh" => result.strDescriptionCN,
                _ => null
            };

        internal async Task EnsureInfo(string musicBrainzReleaseGroupId, CancellationToken cancellationToken)
        {
            var xmlPath = GetAlbumInfoPath(_config.ApplicationPaths, musicBrainzReleaseGroupId);

            var fileInfo = _fileSystem.GetFileSystemInfo(xmlPath);

            if (fileInfo.Exists
                && (DateTime.UtcNow - _fileSystem.GetLastWriteTimeUtc(fileInfo)).TotalDays <= 30)
            {
                return;
            }

            await DownloadInfo(musicBrainzReleaseGroupId, cancellationToken).ConfigureAwait(false);
        }

        internal async Task DownloadInfo(string musicBrainzReleaseGroupId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var url = AudioDbArtistProvider.BaseUrl + "/album-mb.php?i=" + musicBrainzReleaseGroupId;

            var path = GetAlbumInfoPath(_config.ApplicationPaths, musicBrainzReleaseGroupId);
            var fileInfo = _fileSystem.GetFileSystemInfo(path);
            if (fileInfo.Exists && (DateTime.UtcNow - _fileSystem.GetLastWriteTimeUtc(fileInfo)).TotalDays <= 30)
            {
                return;
            }

            // Khylo: never during library scans, and within TheAudioDB's request limits
            if (!MusicEnrichment.MayGoOnline || !await SourceRateLimit.WaitTurnAsync(SourceRateLimit.TheAudioDb, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path));

            using var response = await _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (SourceRateLimit.CheckForPause(SourceRateLimit.TheAudioDb, response) || !response.IsSuccessStatusCode)
            {
                return;
            }

            var fileStreamOptions = AsyncFile.WriteOptions;
            fileStreamOptions.Mode = FileMode.Create;
            var fs = new FileStream(path, fileStreamOptions);
            await using (fs.ConfigureAwait(false))
            {
                await response.Content.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);
            }
        }

        private static string GetAlbumDataPath(IApplicationPaths appPaths, string musicBrainzReleaseGroupId)
        {
            var dataPath = Path.Combine(GetAlbumDataPath(appPaths), musicBrainzReleaseGroupId);

            return dataPath;
        }

        private static string GetAlbumDataPath(IApplicationPaths appPaths)
        {
            var dataPath = Path.Combine(appPaths.CachePath, "audiodb-album");

            return dataPath;
        }

        internal static string GetAlbumInfoPath(IApplicationPaths appPaths, string musicBrainzReleaseGroupId)
        {
            var dataPath = GetAlbumDataPath(appPaths, musicBrainzReleaseGroupId);

            return Path.Combine(dataPath, "album.json");
        }

        /// <inheritdoc />
        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

#pragma warning disable CA1034, CA2227
        public class Album
        {
            public string idAlbum { get; set; }

            public string idArtist { get; set; }

            public string strAlbum { get; set; }

            public string strArtist { get; set; }

            public string intYearReleased { get; set; }

            public string strGenre { get; set; }

            public string strSubGenre { get; set; }

            public string strReleaseFormat { get; set; }

            public string intSales { get; set; }

            public string strAlbumThumb { get; set; }

            public string strAlbumCDart { get; set; }

            public string strDescription { get; set; }

            public string strDescriptionEN { get; set; }

            public string strDescriptionDE { get; set; }

            public string strDescriptionFR { get; set; }

            public string strDescriptionCN { get; set; }

            public string strDescriptionIT { get; set; }

            public string strDescriptionJP { get; set; }

            public string strDescriptionRU { get; set; }

            public string strDescriptionES { get; set; }

            public string strDescriptionPT { get; set; }

            public string strDescriptionSE { get; set; }

            public string strDescriptionNL { get; set; }

            public string strDescriptionHU { get; set; }

            public string strDescriptionNO { get; set; }

            public string strDescriptionIL { get; set; }

            public string strDescriptionPL { get; set; }

            public object intLoved { get; set; }

            public object intScore { get; set; }

            public string strReview { get; set; }

            public object strMood { get; set; }

            public object strTheme { get; set; }

            public object strSpeed { get; set; }

            public object strLocation { get; set; }

            public string strMusicBrainzID { get; set; }

            public string strMusicBrainzArtistID { get; set; }

            public object strItunesID { get; set; }

            public object strAmazonID { get; set; }

            public string strLocked { get; set; }
        }

        public class RootObject
        {
            public List<Album> album { get; set; }
        }
    }
}
