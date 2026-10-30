using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Common.Configuration;
using Khylo.Common.Net;
using Khylo.Controller.Entities;
using Khylo.Controller.Entities.Audio;
using Khylo.Controller.Library;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;
using Khylo.Model.Providers;

namespace Khylo.Providers.Music.Online;

/// <summary>
/// An artist's photo from Deezer (served from the US), for artists TheAudioDB has no photo of. Only an
/// artist with exactly the same name is used, and Deezer's blank placeholder is skipped.
/// </summary>
public class DeezerArtistImageProvider : IRemoteImageProvider, IHasOrder
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IApplicationPaths _paths;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeezerArtistImageProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="paths">The application paths.</param>
    public DeezerArtistImageProvider(IHttpClientFactory httpClientFactory, IApplicationPaths paths)
    {
        _httpClientFactory = httpClientFactory;
        _paths = paths;
    }

    /// <inheritdoc />
    public string Name => "Deezer";

    /// <inheritdoc />
    // After TheAudioDB
    public int Order => 1;

    /// <inheritdoc />
    public bool Supports(BaseItem item) => item is MusicArtist;

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => [ImageType.Primary];

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        // Deezer had no photo of this artist last time: don't ask again on every scan
        if (string.IsNullOrWhiteSpace(item.Name) || !MusicEnrichment.MayGoOnline || OnlineLookupCache.WasLookedUp(_paths, "deezer", item.Name)
            || !await SourceRateLimit.WaitTurnAsync(SourceRateLimit.Deezer, cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        using var http = await _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(
            "https://api.deezer.com/search/artist?limit=10&q=" + Uri.EscapeDataString(item.Name),
            cancellationToken).ConfigureAwait(false);
        if (SourceRateLimit.CheckForPause(SourceRateLimit.Deezer, http) || !http.IsSuccessStatusCode)
        {
            return [];
        }

        var response = await http.Content.ReadFromJsonAsync<SearchResponse>(cancellationToken).ConfigureAwait(false);

        var artist = response?.Data?.FirstOrDefault(a => MusicNameMatch.SameArtist(a.Name, item.Name));
        var url = artist?.PictureXl;

        // Artists without a photo get a grey placeholder with an empty image hash
        if (string.IsNullOrWhiteSpace(url) || url.Contains("/artist//", StringComparison.Ordinal))
        {
            OnlineLookupCache.Record(_paths, "deezer", item.Name, false);
            return [];
        }

        return
        [
            new RemoteImageInfo
            {
                ProviderName = Name,
                Url = url,
                Type = ImageType.Primary,
                Width = 1000,
                Height = 1000
            }
        ];
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(url, cancellationToken);

    private sealed class SearchResponse
    {
        [JsonPropertyName("data")]
        public List<DeezerArtist>? Data { get; set; }
    }

    private sealed class DeezerArtist
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("picture_xl")]
        public string? PictureXl { get; set; }
    }
}
