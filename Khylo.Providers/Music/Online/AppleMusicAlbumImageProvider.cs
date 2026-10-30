using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Common.Net;
using Khylo.Controller.Entities;
using Khylo.Controller.Entities.Audio;
using Khylo.Controller.Library;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;
using Khylo.Model.Providers;

namespace Khylo.Providers.Music.Online;

/// <summary>
/// Album covers from Apple's catalog (hosted in the US), for albums with no cover in their files and
/// none on TheAudioDB. Only an album by the same artist with the same title is used.
/// </summary>
public class AppleMusicAlbumImageProvider : IRemoteImageProvider, IHasOrder
{
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppleMusicAlbumImageProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    public AppleMusicAlbumImageProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc />
    public string Name => "Apple Music";

    /// <inheritdoc />
    // After TheAudioDB
    public int Order => 1;

    /// <inheritdoc />
    public bool Supports(BaseItem item) => item is MusicAlbum;

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => [ImageType.Primary];

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        var artist = (item as MusicAlbum)?.AlbumArtist;
        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(item.Name) || !MusicEnrichment.MayGoOnline
            || !await SourceRateLimit.WaitTurnAsync(SourceRateLimit.Apple, cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        var url = "https://itunes.apple.com/search?media=music&entity=album&limit=25&term=" + Uri.EscapeDataString(artist + " " + item.Name);
        using var response = await _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(url, cancellationToken).ConfigureAwait(false);

        // Apple limits how often the catalog can be searched; when it says slow down, just skip this album
        if (SourceRateLimit.CheckForPause(SourceRateLimit.Apple, response) || response.StatusCode == HttpStatusCode.Forbidden)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();
        var results = await response.Content.ReadFromJsonAsync<SearchResponse>(cancellationToken).ConfigureAwait(false);

        var album = results?.Results?.FirstOrDefault(a => MusicNameMatch.SameArtist(a.ArtistName, artist) && MusicNameMatch.SameAlbum(a.CollectionName, item.Name));
        if (string.IsNullOrWhiteSpace(album?.ArtworkUrl100))
        {
            return [];
        }

        return
        [
            new RemoteImageInfo
            {
                ProviderName = Name,
                Url = album.ArtworkUrl100.Replace("100x100bb", "1400x1400bb", StringComparison.Ordinal),
                Type = ImageType.Primary,
                Width = 1400,
                Height = 1400
            }
        ];
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(url, cancellationToken);

    private sealed class SearchResponse
    {
        [JsonPropertyName("results")]
        public List<Album>? Results { get; set; }
    }

    private sealed class Album
    {
        [JsonPropertyName("artistName")]
        public string? ArtistName { get; set; }

        [JsonPropertyName("collectionName")]
        public string? CollectionName { get; set; }

        [JsonPropertyName("artworkUrl100")]
        public string? ArtworkUrl100 { get; set; }
    }
}
