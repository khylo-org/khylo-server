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

namespace Khylo.Providers.Music.Online;

/// <summary>
/// An artist's photo from Wikimedia Commons, found alongside the Wikipedia biography, for artists
/// TheAudioDB and Deezer have no photo of.
/// </summary>
public class WikimediaArtistImageProvider : IRemoteImageProvider, IHasOrder
{
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="WikimediaArtistImageProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    public WikimediaArtistImageProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc />
    public string Name => "Wikimedia Commons";

    /// <inheritdoc />
    // After TheAudioDB and Deezer
    public int Order => 2;

    /// <inheritdoc />
    public bool Supports(BaseItem item) => item is MusicArtist;

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => [ImageType.Primary];

    /// <inheritdoc />
    public Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        var url = WikipediaArtistProvider.GetPhoto(item.Name);
        IEnumerable<RemoteImageInfo> images = url is null
            ? []
            : [new RemoteImageInfo { ProviderName = Name, Url = url, Type = ImageType.Primary }];
        return Task.FromResult(images);
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(url, cancellationToken);
}
