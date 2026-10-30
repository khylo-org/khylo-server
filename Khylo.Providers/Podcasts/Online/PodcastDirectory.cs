using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Khylo.Providers.Music.Online;

namespace Khylo.Providers.Podcasts.Online;

/// <summary>
/// Finds podcasts in Apple's podcast directory (free, no key) and reads their RSS feeds.
/// </summary>
public static partial class PodcastDirectory
{
    /// <summary>The provider name.</summary>
    public const string ProviderName = "Apple Podcasts";

    /// <summary>The provider id key: Apple's podcast id.</summary>
    public const string IdKey = "ApplePodcasts";

    private static readonly XNamespace _itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";

    /// <summary>
    /// Searches the directory by name.
    /// </summary>
    /// <param name="http">The HTTP client.</param>
    /// <param name="name">The podcast's name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matches, best first.</returns>
    public static async Task<IReadOnlyList<PodcastListing>> SearchAsync(HttpClient http, string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return [];
        }

        var listings = await QueryAsync(http, "https://itunes.apple.com/search?media=podcast&entity=podcast&limit=10&term=" + Uri.EscapeDataString(name), cancellationToken).ConfigureAwait(false);
        return listings.OrderByDescending(l => SameName(l.Name, name)).ToList();
    }

    /// <summary>
    /// Looks up one podcast by Apple's id.
    /// </summary>
    /// <param name="http">The HTTP client.</param>
    /// <param name="id">Apple's podcast id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The podcast, or null.</returns>
    public static async Task<PodcastListing?> LookupAsync(HttpClient http, string id, CancellationToken cancellationToken)
        => (await QueryAsync(http, "https://itunes.apple.com/lookup?entity=podcast&id=" + Uri.EscapeDataString(id), cancellationToken).ConfigureAwait(false)).FirstOrDefault();

    /// <summary>
    /// Reads a podcast's RSS feed for its description, author, website, categories and artwork.
    /// </summary>
    /// <param name="http">The HTTP client.</param>
    /// <param name="feedUrl">The feed address.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The feed's details, or null.</returns>
    public static async Task<PodcastFeedInfo?> ReadFeedAsync(HttpClient http, string feedUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(feedUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                var settings = new XmlReaderSettings { Async = true, DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
                using var reader = XmlReader.Create(stream, settings);
                var doc = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken).ConfigureAwait(false);
                var channel = doc.Descendants("channel").FirstOrDefault();
                if (channel is null)
                {
                    return null;
                }

                return new PodcastFeedInfo
                {
                    Title = ((string?)channel.Element("title"))?.Trim(),
                    Description = CleanHtml((string?)channel.Element(_itunes + "summary") ?? (string?)channel.Element("description")),
                    Author = ((string?)channel.Element(_itunes + "author") ?? (string?)channel.Element("managingEditor"))?.Trim(),
                    Website = ((string?)channel.Element("link"))?.Trim(),
                    ImageUrl = (string?)channel.Element(_itunes + "image")?.Attribute("href") ?? (string?)channel.Element("image")?.Element("url"),
                    Categories = channel.Descendants(_itunes + "category").Select(c => ((string?)c.Attribute("text"))?.Trim()).OfType<string>()
                        .Where(c => c.Length > 0).Distinct().Take(4).ToList(),
                };
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or XmlException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether two podcast names are the same show (ignoring case, punctuation and a leading "The").
    /// </summary>
    /// <param name="a">One name.</param>
    /// <param name="b">The other.</param>
    /// <returns>True when they match.</returns>
    public static bool SameName(string? a, string? b)
    {
        static string Key(string? s)
        {
            var lower = (s ?? string.Empty).ToLowerInvariant().Replace("&", "and", StringComparison.Ordinal);
            lower = lower.StartsWith("the ", StringComparison.Ordinal) ? lower[4..] : lower;
            return new string(lower.Where(char.IsLetterOrDigit).ToArray());
        }

        var x = Key(a);
        return x.Length > 0 && x == Key(b);
    }

    /// <summary>
    /// Whether one podcast name is the other with a few words added ("Park Postcards" and
    /// "Park Postcards Podcast"): close enough when no exact match exists.
    /// </summary>
    /// <param name="a">One name.</param>
    /// <param name="b">The other.</param>
    /// <returns>True when they nearly match.</returns>
    public static bool CloseName(string? a, string? b)
    {
        static string Key(string? s) => new((s ?? string.Empty).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        var x = Key(a);
        var y = Key(b);
        return x.Length >= 8 && y.Length >= 8 && (x.Contains(y, StringComparison.Ordinal) || y.Contains(x, StringComparison.Ordinal));
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTag();

    private static string? CleanHtml(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var plain = HtmlTag().Replace(text.Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase).Replace("</p>", "\n\n", StringComparison.OrdinalIgnoreCase), string.Empty);
        return System.Net.WebUtility.HtmlDecode(plain).Trim();
    }

    private static async Task<List<PodcastListing>> QueryAsync(HttpClient http, string url, CancellationToken cancellationToken)
    {
        // Apple limits how often its directory can be searched (shared with album cover lookups)
        if (!await SourceRateLimit.WaitTurnAsync(SourceRateLimit.Apple, cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        try
        {
            using var response = await http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (SourceRateLimit.CheckForPause(SourceRateLimit.Apple, response) || !response.IsSuccessStatusCode)
            {
                return [];
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!doc.RootElement.TryGetProperty("results", out var results))
                {
                    return [];
                }

                return results.EnumerateArray()
                    .Where(r => Str(r, "kind") == "podcast" || r.TryGetProperty("feedUrl", out _))
                    .Select(r => new PodcastListing
                    {
                        Id = r.TryGetProperty("collectionId", out var id) ? id.GetInt64().ToString(CultureInfo.InvariantCulture) : string.Empty,
                        Name = Str(r, "collectionName"),
                        Author = Str(r, "artistName"),
                        FeedUrl = Str(r, "feedUrl"),
                        ArtworkUrl = Str(r, "artworkUrl600") ?? Str(r, "artworkUrl100"),
                        Genre = Str(r, "primaryGenreName"),
                    })
                    .Where(l => l.Id.Length > 0)
                    .ToList();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return [];
        }
    }

    private static string? Str(JsonElement element, string name)
        => element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
