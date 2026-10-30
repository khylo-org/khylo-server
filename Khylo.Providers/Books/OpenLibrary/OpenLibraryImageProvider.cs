using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Common.Net;
using Khylo.Controller.Entities;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;
using Khylo.Model.Providers;

namespace Khylo.Providers.Books.OpenLibrary;

/// <summary>
/// Covers from Open Library, for books and audiobooks that have no cover of their own (in the file or the folder).
/// </summary>
public class OpenLibraryImageProvider : IRemoteImageProvider, IHasOrder
{
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenLibraryImageProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    public OpenLibraryImageProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc />
    public string Name => OpenLibrarySource.ProviderName;

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public bool Supports(BaseItem item) => item is Book or AudioBook;

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => [ImageType.Primary];

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        var http = _httpClientFactory.CreateClient(NamedClient.Default);
        var id = item.GetProviderId(OpenLibrarySource.IdKey);
        if (string.IsNullOrEmpty(id))
        {
            var title = item is AudioBook book && !string.IsNullOrWhiteSpace(book.Album) ? book.Album : item.Name;
            var author = item is AudioBook ab
                ? (ab.AlbumArtists.Count > 0 ? ab.AlbumArtists[0] : ab.Artists.Count > 0 ? ab.Artists[0] : null)
                : BookTitle.AuthorFromName(item.Name);
            id = (await OpenLibrarySource.FindAsync(http, title, author, cancellationToken).ConfigureAwait(false))?.Id;
        }

        var work = string.IsNullOrEmpty(id) ? null : await OpenLibrarySource.GetWorkAsync(http, id, cancellationToken).ConfigureAwait(false);
        return work?.Covers.Take(5).Select(cover => new RemoteImageInfo
        {
            ProviderName = Name,
            Type = ImageType.Primary,
            Url = OpenLibrarySource.CoverUrl(cover, "L"),
        }).ToList() ?? [];
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(url, cancellationToken);
}
