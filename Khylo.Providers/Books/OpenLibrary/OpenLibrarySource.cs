using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Model.Entities;
using Khylo.Model.Providers;

namespace Khylo.Providers.Books.OpenLibrary;

/// <summary>
/// Open Library (openlibrary.org, the Internet Archive's free book catalog, no key needed): finds a book by
/// title and author and reads its description, subjects and cover. Used for books and audiobooks.
/// </summary>
public static class OpenLibrarySource
{
    /// <summary>The name shown for results from Open Library.</summary>
    public const string ProviderName = "Open Library";

    /// <summary>The provider id key: an Open Library work, e.g. OL450063W.</summary>
    public const string IdKey = "OpenLibrary";

    // Subjects worth showing as genres, in the words people use. Matched as whole words, so "war" doesn't
    // catch "warfare" and "western" needs "western stories"
    private static readonly (Regex Match, string Genre)[] _genres =
    [
        .. new (string Pattern, string Genre)[]
        {
            ("science fiction", "Science Fiction"), ("time travel", "Science Fiction"), ("fantasy", "Fantasy"), ("fairy tales?", "Fairy Tales"),
            ("horror", "Horror"), ("ghost stories", "Horror"), ("vampires?", "Horror"), ("gothic", "Gothic"),
            ("detective and mystery", "Mystery"), ("mystery", "Mystery"), ("private investigators", "Mystery"), ("crime", "Crime"),
            ("love stories", "Romance"), ("romance", "Romance"), ("adventure stories", "Adventure"), ("adventure", "Adventure"),
            ("sea stories", "Adventure"), ("pirates", "Adventure"), ("juvenile fiction", "Children's"), ("children's stories", "Children's"),
            ("humorous stories", "Humor"), ("humor", "Humor"), ("satire", "Humor"), ("war stories", "War"), ("historical fiction", "Historical Fiction"),
            ("poetry", "Poetry"), ("epic poetry", "Poetry"), ("drama", "Drama"), ("tragedies", "Drama"), ("philosophy", "Philosophy"),
            ("stoics", "Philosophy"), ("ethics", "Philosophy"), ("military art and science", "Strategy"), ("biography", "Biography"),
            ("autobiography", "Biography"), ("comics", "Comics"), ("western stories", "Western"),
            ("christmas stories", "Holiday"), ("psychological fiction", "Literary Fiction"), ("domestic fiction", "Literary Fiction"),
            ("classic literature", "Classics"), ("dogs", "Animals"), ("animals", "Animals"),
        }.Select(g => (new Regex(@"\b(?:" + g.Pattern + @")\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), g.Genre)),
    ];

    /// <summary>
    /// Searches for a book.
    /// </summary>
    /// <param name="http">The HTTP client.</param>
    /// <param name="title">The title.</param>
    /// <param name="author">The author, if known.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matches, best first.</returns>
    public static async Task<IReadOnlyList<RemoteSearchResult>> SearchAsync(HttpClient http, string title, string? author, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return [];
        }

        var url = "https://openlibrary.org/search.json?limit=8&fields=key,title,author_name,first_publish_year,cover_i,subject&title=" + Uri.EscapeDataString(title)
            + (string.IsNullOrWhiteSpace(author) ? string.Empty : "&author=" + Uri.EscapeDataString(author));
        using var doc = await GetJsonAsync(http, url, cancellationToken).ConfigureAwait(false);
        if (doc is null || !doc.RootElement.TryGetProperty("docs", out var docs))
        {
            return [];
        }

        var results = new List<RemoteSearchResult>();
        foreach (var book in docs.EnumerateArray())
        {
            var key = Str(book, "key");
            if (key is null)
            {
                continue;
            }

            var result = new RemoteSearchResult
            {
                Name = Str(book, "title"),
                SearchProviderName = ProviderName,
                ProductionYear = book.TryGetProperty("first_publish_year", out var year) && year.TryGetInt32(out var y) ? y : null,
            };
            result.SetProviderId(IdKey, key.Replace("/works/", string.Empty, StringComparison.Ordinal));
            if (book.TryGetProperty("cover_i", out var cover) && cover.TryGetInt64(out var coverId))
            {
                result.ImageUrl = CoverUrl(coverId, "M");
            }

            var authors = Strings(book, "author_name");
            if (authors.Count > 0)
            {
                result.Overview = "by " + string.Join(", ", authors.Take(2));
            }

            results.Add(result);
        }

        // Books by the right author first, then the closest title
        return results
            .OrderByDescending(r => !string.IsNullOrWhiteSpace(author) && (r.Overview ?? string.Empty).Contains(LastName(author), StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(r => string.Equals(BookTitle.Clean(r.Name), BookTitle.Clean(title), StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Reads one work: its description, genres and covers.
    /// </summary>
    /// <param name="http">The HTTP client.</param>
    /// <param name="workId">The work id, e.g. OL450063W.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The work, or null.</returns>
    public static async Task<OpenLibraryWork?> GetWorkAsync(HttpClient http, string workId, CancellationToken cancellationToken)
    {
        using var doc = await GetJsonAsync(http, "https://openlibrary.org/works/" + Uri.EscapeDataString(workId) + ".json", cancellationToken).ConfigureAwait(false);
        if (doc is null)
        {
            return null;
        }

        var root = doc.RootElement;
        string? description = null;
        if (root.TryGetProperty("description", out var d))
        {
            description = d.ValueKind == JsonValueKind.String ? d.GetString() : Str(d, "value");
        }

        var covers = root.TryGetProperty("covers", out var c) && c.ValueKind == JsonValueKind.Array
            ? c.EnumerateArray().Where(x => x.TryGetInt64(out var id) && id > 0).Select(x => x.GetInt64()).ToList()
            : [];

        return new OpenLibraryWork
        {
            Id = workId,
            Title = Str(root, "title"),
            Description = CleanDescription(description),
            Genres = PickGenres(Strings(root, "subjects")),
            Covers = covers,
        };
    }

    /// <summary>
    /// Finds the work for a title and author: the best search match whose title really matches.
    /// </summary>
    /// <param name="http">The HTTP client.</param>
    /// <param name="title">The title.</param>
    /// <param name="author">The author, if known.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The work id and first publication year, or null.</returns>
    public static async Task<(string Id, int? Year)?> FindAsync(HttpClient http, string title, string? author, CancellationToken cancellationToken)
    {
        var clean = BookTitle.Clean(title);
        var results = await SearchAsync(http, clean, author, cancellationToken).ConfigureAwait(false);
        var match = results.FirstOrDefault(r => BookTitle.SameBook(r.Name, clean));
        var id = match?.GetProviderId(IdKey);
        return id is null ? null : (id, match!.ProductionYear);
    }

    /// <summary>
    /// The address of an Open Library cover.
    /// </summary>
    /// <param name="coverId">The cover id.</param>
    /// <param name="size">S, M or L.</param>
    /// <returns>The URL.</returns>
    public static string CoverUrl(long coverId, string size) => $"https://covers.openlibrary.org/b/id/{coverId.ToString(CultureInfo.InvariantCulture)}-{size}.jpg";

    /// <summary>
    /// Turns Open Library's subjects into a few genres people recognise.
    /// </summary>
    /// <param name="subjects">The subjects.</param>
    /// <returns>Up to four genres.</returns>
    public static IReadOnlyList<string> PickGenres(IEnumerable<string> subjects)
    {
        // Subjects come most important first; each gives the first genre it matches
        var genres = new List<string>();
        foreach (var subject in subjects.Take(40))
        {
            foreach (var (match, genre) in _genres)
            {
                if (match.IsMatch(subject))
                {
                    if (!genres.Contains(genre))
                    {
                        genres.Add(genre);
                    }

                    break;
                }
            }

            if (genres.Count == 3)
            {
                break;
            }
        }

        return genres;
    }

    private static string LastName(string author) => author.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? author;

    private static string? CleanDescription(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // Open Library descriptions often end with a source line or a list of editions after a rule
        var cut = text.IndexOf("\r\n----", StringComparison.Ordinal);
        if (cut < 0)
        {
            cut = text.IndexOf("\n----", StringComparison.Ordinal);
        }

        return (cut > 0 ? text[..cut] : text).Trim();
    }

    private static async Task<JsonDocument?> GetJsonAsync(HttpClient http, string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
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

    private static string? Str(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static List<string> Strings(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
            : [];
}
