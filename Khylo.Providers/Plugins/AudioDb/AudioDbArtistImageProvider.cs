#nullable disable

#pragma warning disable CS1591

using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Common.Net;
using Khylo.Controller.Entities;
using Khylo.Controller.Entities.Audio;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;
using Khylo.Model.Providers;

namespace Khylo.Providers.Plugins.AudioDb
{
    public class AudioDbArtistImageProvider : IRemoteImageProvider, IHasOrder
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public AudioDbArtistImageProvider(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        /// <inheritdoc />
        public string Name => "TheAudioDB";

        /// <inheritdoc />
        // Khylo: first, it's US-hosted and can match by name
        public int Order => 0;

        /// <inheritdoc />
        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            return
            [
                ImageType.Primary,
                ImageType.Logo,
                ImageType.Banner,
                ImageType.Backdrop
            ];
        }

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            item.TryGetProviderId(MetadataProvider.MusicBrainzArtist, out var musicBrainzId);
            item.TryGetProviderId(MetadataProvider.AudioDbArtist, out var audioDbId);

            var artist = await AudioDbArtistProvider.Current.GetArtist(musicBrainzId, audioDbId, item.Name, cancellationToken).ConfigureAwait(false);

            if (artist is not null)
            {
                return GetImages(artist);
            }

            return [];
        }

        private List<RemoteImageInfo> GetImages(AudioDbArtistProvider.Artist item)
        {
            var list = new List<RemoteImageInfo>();

            if (!string.IsNullOrWhiteSpace(item.strArtistThumb))
            {
                list.Add(new RemoteImageInfo
                {
                    ProviderName = Name,
                    Url = item.strArtistThumb,
                    Type = ImageType.Primary
                });
            }

            if (!string.IsNullOrWhiteSpace(item.strArtistLogo))
            {
                list.Add(new RemoteImageInfo
                {
                    ProviderName = Name,
                    Url = item.strArtistLogo,
                    Type = ImageType.Logo
                });
            }

            if (!string.IsNullOrWhiteSpace(item.strArtistBanner))
            {
                list.Add(new RemoteImageInfo
                {
                    ProviderName = Name,
                    Url = item.strArtistBanner,
                    Type = ImageType.Banner
                });
            }

            if (!string.IsNullOrWhiteSpace(item.strArtistFanart))
            {
                list.Add(new RemoteImageInfo
                {
                    ProviderName = Name,
                    Url = item.strArtistFanart,
                    Type = ImageType.Backdrop
                });
            }

            if (!string.IsNullOrWhiteSpace(item.strArtistFanart2))
            {
                list.Add(new RemoteImageInfo
                {
                    ProviderName = Name,
                    Url = item.strArtistFanart2,
                    Type = ImageType.Backdrop
                });
            }

            if (!string.IsNullOrWhiteSpace(item.strArtistFanart3))
            {
                list.Add(new RemoteImageInfo
                {
                    ProviderName = Name,
                    Url = item.strArtistFanart3,
                    Type = ImageType.Backdrop
                });
            }

            return list;
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            var httpClient = _httpClientFactory.CreateClient(NamedClient.Default);
            return httpClient.GetAsync(url, cancellationToken);
        }

        /// <inheritdoc />
        public bool Supports(BaseItem item)
            => item is MusicArtist;
    }
}
