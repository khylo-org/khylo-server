using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Common.Net;
using Khylo.Controller.Entities;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;
using Khylo.Model.Providers;

namespace Khylo.Providers.Podcasts.Online;

/// <summary>
/// Podcast artwork from the show's feed or Apple's podcast directory, for shows with no cover in their folder.
/// </summary>
public class ApplePodcastsImageProvider : IRemoteImageProvider, IHasOrder
{
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApplePodcastsImageProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    public ApplePodcastsImageProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc />
    public string Name => PodcastDirectory.ProviderName;

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public bool Supports(BaseItem item) => item is Podcast;

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => [ImageType.Primary];

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        var http = _httpClientFactory.CreateClient(NamedClient.Default);
        var podcast = (Podcast)item;
        var images = new List<RemoteImageInfo>();

        if (!string.IsNullOrEmpty(podcast.FeedUrl)
            && await PodcastDirectory.ReadFeedAsync(http, podcast.FeedUrl, cancellationToken).ConfigureAwait(false) is { ImageUrl: { Length: > 0 } feedImage })
        {
            images.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Primary, Url = feedImage });
        }

        var id = item.GetProviderId(PodcastDirectory.IdKey);
        var listing = string.IsNullOrEmpty(id) ? null : await PodcastDirectory.LookupAsync(http, id, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(listing?.ArtworkUrl))
        {
            images.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Primary, Url = listing.ArtworkUrl });
        }

        return images;
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(url, cancellationToken);
}
