using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Common.Net;
using Khylo.Controller.Entities;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;
using Khylo.Model.Providers;
using Khylo.Providers.Books.OpenLibrary;

namespace Khylo.Providers.Books.GoogleBooks;

/// <summary>
/// Book details from Google Books (no key needed for occasional lookups), after Open Library: more matches
/// to choose from in "Identify", and a description and categories when Open Library has none.
/// </summary>
public partial class GoogleBooksProvider : IRemoteMetadataProvider<Book, BookInfo>, IHasOrder
{
    private const string IdKey = "GoogleBooks";
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="GoogleBooksProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    public GoogleBooksProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc />
    public string Name => "Google Books";

    /// <inheritdoc />
    public int Order => 1;

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(BookInfo searchInfo, CancellationToken cancellationToken)
    {
        var id = searchInfo.GetProviderId(IdKey);
        var volumes = string.IsNullOrEmpty(id)
            ? await SearchAsync(BookTitle.Clean(searchInfo.Name), BookTitle.AuthorFromName(searchInfo.Name), cancellationToken).ConfigureAwait(false)
            : await GetVolumeAsync(id, cancellationToken).ConfigureAwait(false) is { } one ? [one] : [];
        return volumes.Select(ToResult).ToList();
    }

    /// <inheritdoc />
    public async Task<MetadataResult<Book>> GetMetadata(BookInfo info, CancellationToken cancellationToken)
    {
        var id = info.GetProviderId(IdKey);
        JsonElement? volume = null;
        if (!string.IsNullOrEmpty(id))
        {
            volume = await GetVolumeAsync(id, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var found = await SearchAsync(BookTitle.Clean(info.Name), BookTitle.AuthorFromName(info.Name), cancellationToken).ConfigureAwait(false);
            var match = found.Where(v => BookTitle.SameBook(Str(Info(v), "title"), info.Name)).Cast<JsonElement?>().FirstOrDefault();
            volume = match;
        }

        if (volume is not { } v)
        {
            return new MetadataResult<Book> { HasMetadata = false };
        }

        var volumeInfo = Info(v);
        var book = new Book { Name = Str(volumeInfo, "title"), Overview = CleanHtml(Str(volumeInfo, "description")), ProductionYear = Year(volumeInfo) };
        book.SetProviderId(IdKey, Str(v, "id") ?? string.Empty);
        foreach (var category in Strings(volumeInfo, "categories").SelectMany(c => c.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)).Distinct().Take(3))
        {
            book.AddGenre(category);
        }

        return new MetadataResult<Book> { Item = book, HasMetadata = !string.IsNullOrEmpty(book.Overview) || book.Genres.Length > 0 };
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(url, cancellationToken);

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTag();

    private static string? CleanHtml(string? text) => string.IsNullOrWhiteSpace(text) ? null : HtmlTag().Replace(text.Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase), string.Empty).Trim();

    private static JsonElement Info(JsonElement volume) => volume.TryGetProperty("volumeInfo", out var v) ? v : default;

    private static int? Year(JsonElement info)
        => Str(info, "publishedDate") is { Length: >= 4 } date && int.TryParse(date.AsSpan(0, 4), out var y) ? y : null;

    private static string? Str(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static List<string> Strings(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
            : [];

    private RemoteSearchResult ToResult(JsonElement volume)
    {
        var info = Info(volume);
        var authors = Strings(info, "authors");
        var result = new RemoteSearchResult
        {
            Name = Str(info, "title"),
            ProductionYear = Year(info),
            Overview = authors.Count > 0 ? "by " + string.Join(", ", authors.Take(2)) : null,
            SearchProviderName = Name,
        };
        result.SetProviderId(IdKey, Str(volume, "id") ?? string.Empty);
        if (info.ValueKind == JsonValueKind.Object && info.TryGetProperty("imageLinks", out var links))
        {
            result.ImageUrl = (Str(links, "thumbnail") ?? Str(links, "smallThumbnail"))?.Replace("http://", "https://", StringComparison.Ordinal);
        }

        return result;
    }

    private async Task<List<JsonElement>> SearchAsync(string title, string? author, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return [];
        }

        var query = "intitle:" + title + (string.IsNullOrWhiteSpace(author) ? string.Empty : " inauthor:" + author);
        using var doc = await GetJsonAsync("https://www.googleapis.com/books/v1/volumes?printType=books&maxResults=8&q=" + Uri.EscapeDataString(query), cancellationToken).ConfigureAwait(false);
        return doc is not null && doc.RootElement.TryGetProperty("items", out var items)
            ? items.EnumerateArray().Select(i => i.Clone()).ToList()
            : [];
    }

    private async Task<JsonElement?> GetVolumeAsync(string id, CancellationToken cancellationToken)
    {
        using var doc = await GetJsonAsync("https://www.googleapis.com/books/v1/volumes/" + Uri.EscapeDataString(id), cancellationToken).ConfigureAwait(false);
        return doc?.RootElement.Clone();
    }

    private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // Without a key Google allows a limited number of lookups; past it, just go without
                return null;
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
