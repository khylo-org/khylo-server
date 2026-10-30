using System.Collections.Generic;
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
/// Book details from Open Library: description, genres and first publication year. Details written in the
/// book's own file (EPUB) come first; this fills what they leave out, and powers "Identify" for books.
/// </summary>
public class OpenLibraryBookProvider : IRemoteMetadataProvider<Book, BookInfo>, IHasOrder
{
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenLibraryBookProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    public OpenLibraryBookProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc />
    public string Name => OpenLibrarySource.ProviderName;

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(BookInfo searchInfo, CancellationToken cancellationToken)
    {
        var http = _httpClientFactory.CreateClient(NamedClient.Default);
        var id = searchInfo.GetProviderId(OpenLibrarySource.IdKey);
        if (!string.IsNullOrEmpty(id))
        {
            var work = await OpenLibrarySource.GetWorkAsync(http, id, cancellationToken).ConfigureAwait(false);
            if (work is not null)
            {
                var result = new RemoteSearchResult { Name = work.Title, Overview = work.Description, SearchProviderName = Name };
                result.SetProviderId(OpenLibrarySource.IdKey, work.Id);
                if (work.Covers.Count > 0)
                {
                    result.ImageUrl = OpenLibrarySource.CoverUrl(work.Covers[0], "M");
                }

                return [result];
            }
        }

        return await OpenLibrarySource.SearchAsync(http, BookTitle.Clean(searchInfo.Name), BookTitle.AuthorFromName(searchInfo.Name), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<MetadataResult<Book>> GetMetadata(BookInfo info, CancellationToken cancellationToken)
    {
        var http = _httpClientFactory.CreateClient(NamedClient.Default);
        var id = info.GetProviderId(OpenLibrarySource.IdKey);
        int? year = null;
        // The search also gives the year the book first came out
        {
            var found = await OpenLibrarySource.FindAsync(http, info.Name, BookTitle.AuthorFromName(info.Name), cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(id) || found?.Id == id)
            {
                if (string.IsNullOrEmpty(id))
                {
                    id = found?.Id;
                }

                year = found?.Year;
            }
        }

        var work = string.IsNullOrEmpty(id) ? null : await OpenLibrarySource.GetWorkAsync(http, id, cancellationToken).ConfigureAwait(false);
        if (work is null)
        {
            return new MetadataResult<Book> { HasMetadata = false };
        }

        // The name the book's own file gives stays; a name taken from a file name ("Title - Author") is replaced
        var book = new Book
        {
            Name = BookTitle.AuthorFromName(info.Name) is null ? null : BookTitle.Tidy(work.Title ?? BookTitle.Clean(info.Name)),
            Overview = work.Description,
            ProductionYear = year ?? info.Year,
        };
        book.SetProviderId(OpenLibrarySource.IdKey, work.Id);
        foreach (var genre in work.Genres)
        {
            book.AddGenre(genre);
        }

        return new MetadataResult<Book> { Item = book, HasMetadata = true };
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(url, cancellationToken);
}
