#nullable disable

#pragma warning disable CS1591

using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Common.Net;
using Khylo.Controller.Configuration;
using Khylo.Controller.Entities;
using Khylo.Controller.Entities.Audio;
using Khylo.Controller.Providers;
using Khylo.Extensions.Json;
using Khylo.Model.Entities;
using Khylo.Model.IO;
using Khylo.Model.Providers;

namespace Khylo.Providers.Plugins.AudioDb
{
    public class AudioDbAlbumImageProvider : IRemoteImageProvider, IHasOrder
    {
        private readonly IServerConfigurationManager _config;
        private readonly IHttpClientFactory _httpClientFactory;

        public AudioDbAlbumImageProvider(IServerConfigurationManager config, IHttpClientFactory httpClientFactory)
        {
            _config = config;
            _httpClientFactory = httpClientFactory;
        }

        /// <inheritdoc />
        public string Name => "TheAudioDB";

        /// <inheritdoc />
        // Khylo: first online source; embedded art still comes before any of them
        public int Order => 0;

        /// <inheritdoc />
        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            yield return ImageType.Primary;
            yield return ImageType.Disc;
        }

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            item.TryGetProviderId(MetadataProvider.MusicBrainzReleaseGroup, out var id);
            var album = await AudioDbAlbumProvider.Current.GetAlbum(id, (item as MusicAlbum)?.AlbumArtist, item.Name, cancellationToken).ConfigureAwait(false);

            return album is null ? [] : GetImages(album);
        }

        private List<RemoteImageInfo> GetImages(AudioDbAlbumProvider.Album item)
        {
            var list = new List<RemoteImageInfo>();

            if (!string.IsNullOrWhiteSpace(item.strAlbumThumb))
            {
                list.Add(new RemoteImageInfo
                {
                    ProviderName = Name,
                    Url = item.strAlbumThumb,
                    Type = ImageType.Primary
                });
            }

            if (!string.IsNullOrWhiteSpace(item.strAlbumCDart))
            {
                list.Add(new RemoteImageInfo
                {
                    ProviderName = Name,
                    Url = item.strAlbumCDart,
                    Type = ImageType.Disc
                });
            }

            return list;
        }

        /// <inheritdoc />
        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            var httpClient = _httpClientFactory.CreateClient(NamedClient.Default);
            return httpClient.GetAsync(url, cancellationToken);
        }

        /// <inheritdoc />
        public bool Supports(BaseItem item)
            => item is MusicAlbum;
    }
}
