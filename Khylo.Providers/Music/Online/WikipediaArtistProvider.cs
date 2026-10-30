using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Common.Configuration;
using Khylo.Common.Net;
using Khylo.Controller.Entities.Audio;
using Khylo.Controller.Library;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;
using Khylo.Model.Providers;

namespace Khylo.Providers.Music.Online;

/// <summary>
/// Artist facts from Wikidata and the biography from Wikipedia (both hosted in the US), looked up in
/// batches: one Wikidata query covers up to 25 artists and one Wikipedia request brings their opening
/// paragraphs. Only Wikidata entries carrying a MusicBrainz artist id count, so a band called "Nature"
/// never picks up the article about nature. The MusicBrainz id is kept on the artist, so later lookups
/// go by id instead of by name.
/// </summary>
public class WikipediaArtistProvider : IRemoteMetadataProvider<MusicArtist, ArtistInfo>, IHasOrder
{
    private const string UserAgent = "Khylo/1.0 (self-hosted media server; music artist biographies)";
    private const int BatchSize = 25;

    // What's been fetched this session, by normalised name (null: looked up, nothing found)
    private static readonly ConcurrentDictionary<string, ArtistFacts?> _facts = new(StringComparer.Ordinal);
    private static readonly SemaphoreSlim _batchLock = new(1, 1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IApplicationPaths _paths;

    /// <summary>
    /// Initializes a new instance of the <see cref="WikipediaArtistProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="paths">The application paths.</param>
    public WikipediaArtistProvider(IHttpClientFactory httpClientFactory, IApplicationPaths paths)
    {
        _httpClientFactory = httpClientFactory;
        _paths = paths;
    }

    /// <inheritdoc />
    public string Name => "Wikipedia";

    /// <inheritdoc />
    // First, so the MusicBrainz id it finds lets TheAudioDB look the artist up exactly in the same refresh
    public int Order => -1;

    /// <summary>
    /// Gets the Wikimedia Commons photo found for an artist, if any (used by the artist image provider).
    /// </summary>
    /// <param name="name">The artist's name.</param>
    /// <returns>The image address, or null.</returns>
    public static string? GetPhoto(string? name)
        => _facts.TryGetValue(MusicNameMatch.Normalize(name), out var facts) ? facts?.Image : null;

    /// <inheritdoc />
    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(ArtistInfo searchInfo, CancellationToken cancellationToken)
        => Task.FromResult(Enumerable.Empty<RemoteSearchResult>());

    /// <inheritdoc />
    public async Task<MetadataResult<MusicArtist>> GetMetadata(ArtistInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<MusicArtist>();
        var key = MusicNameMatch.Normalize(info.Name);
        if (key.Length == 0)
        {
            return result;
        }

        if (!_facts.ContainsKey(key))
        {
            // Already looked up before (what was found is saved on the artist), or not allowed online now
            if (!MusicEnrichment.MayGoOnline || OnlineLookupCache.WasLookedUp(_paths, "wikipedia", info.Name))
            {
                return result;
            }

            // This artist plus the others the background worker is about to do, in one go
            var names = new[] { info.Name! }
                .Concat(MusicEnrichment.UpcomingArtistNames)
                .Where(n => !string.IsNullOrWhiteSpace(n) && !_facts.ContainsKey(MusicNameMatch.Normalize(n)) && !OnlineLookupCache.WasLookedUp(_paths, "wikipedia", n))
                .DistinctBy(MusicNameMatch.Normalize)
                .Take(BatchSize)
                .ToList();
            await FetchBatchAsync(names, cancellationToken).ConfigureAwait(false);
        }

        if (!_facts.TryGetValue(key, out var facts) || facts is null)
        {
            return result;
        }

        result.Item = new MusicArtist { Overview = facts.Bio };
        if (!string.IsNullOrEmpty(facts.MusicBrainzId))
        {
            result.Item.SetProviderId(MetadataProvider.MusicBrainzArtist, facts.MusicBrainzId);
        }

        result.HasMetadata = true;
        result.ResultLanguage = "en";
        return result;
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(url, cancellationToken);

    private async Task FetchBatchAsync(List<string> names, CancellationToken cancellationToken)
    {
        await _batchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            names = names.Where(n => !_facts.ContainsKey(MusicNameMatch.Normalize(n))).ToList();
            if (names.Count == 0)
            {
                return;
            }

            var found = await QueryWikidataAsync(names, cancellationToken).ConfigureAwait(false);
            if (found is null)
            {
                // Not answered (busy or slow): leave these for a later lookup
                return;
            }

            var bios = await GetBiosAsync(found.Values.Select(f => f.WikiTitle).OfType<string>().Distinct().ToList(), cancellationToken).ConfigureAwait(false);
            foreach (var name in names)
            {
                var key = MusicNameMatch.Normalize(name);
                ArtistFacts? facts = null;
                if (found.TryGetValue(key, out var f))
                {
                    f.Bio = f.WikiTitle is not null && bios is not null && bios.TryGetValue(f.WikiTitle, out var bio) ? bio : null;
                    facts = f;
                }

                _facts[key] = facts;

                // Only a full answer is remembered; a missing bio because Wikipedia was busy is tried again later
                if (bios is not null || facts is null)
                {
                    OnlineLookupCache.Record(_paths, "wikipedia", name, facts?.Bio is not null || facts?.Image is not null);
                }
            }
        }
        finally
        {
            _batchLock.Release();
        }
    }

    // One Wikidata query: which of these names are music artists, with their MusicBrainz id, English
    // Wikipedia article and photo. A name that fits two different artists is left alone.
    private async Task<Dictionary<string, ArtistFacts>?> QueryWikidataAsync(List<string> names, CancellationToken cancellationToken)
    {
        var variants = names
            .SelectMany(n => new[] { n.Trim(), n.Trim().ToLowerInvariant(), System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(n.Trim().ToLowerInvariant()) })
            .Distinct(StringComparer.Ordinal)
            .Select(v => "\"" + v.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"@en");

        var sparql = "SELECT ?name ?item ?mbid ?article ?image WHERE { VALUES ?name { " + string.Join(' ', variants) + " } "
            + "?item rdfs:label|skos:altLabel ?name ; wdt:P434 ?mbid . "
            + "OPTIONAL { ?article schema:about ?item ; schema:isPartOf <https://en.wikipedia.org/> . } "
            + "OPTIONAL { ?item wdt:P18 ?image } }";

        using var doc = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "https://query.wikidata.org/sparql")
            {
                Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("query", sparql), new KeyValuePair<string, string>("format", "json")])
            },
            cancellationToken).ConfigureAwait(false);
        if (doc is null)
        {
            return null;
        }

        var byName = new Dictionary<string, Dictionary<string, ArtistFacts>>(StringComparer.Ordinal);
        foreach (var row in doc.RootElement.GetProperty("results").GetProperty("bindings").EnumerateArray())
        {
            var key = MusicNameMatch.Normalize(Value(row, "name"));
            var item = Value(row, "item");
            if (key.Length == 0 || item is null)
            {
                continue;
            }

            if (!byName.TryGetValue(key, out var items))
            {
                byName[key] = items = new Dictionary<string, ArtistFacts>(StringComparer.Ordinal);
            }

            if (!items.TryGetValue(item, out var facts))
            {
                items[item] = facts = new ArtistFacts();
            }

            facts.MusicBrainzId ??= Value(row, "mbid");
            facts.Image ??= Value(row, "image") is { } image ? image.Replace("http://", "https://", StringComparison.Ordinal) + "?width=1200" : null;
            if (facts.WikiTitle is null && Value(row, "article") is { } article)
            {
                facts.WikiTitle = Uri.UnescapeDataString(article[(article.LastIndexOf('/') + 1)..]).Replace('_', ' ');
            }
        }

        return byName.Where(e => e.Value.Count == 1).ToDictionary(e => e.Key, e => e.Value.Values.First(), StringComparer.Ordinal);
    }

    // The opening paragraphs of up to 20 Wikipedia articles per request
    private async Task<Dictionary<string, string>?> GetBiosAsync(List<string> titles, CancellationToken cancellationToken)
    {
        var bios = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in titles.Chunk(20))
        {
            var url = "https://en.wikipedia.org/w/api.php?action=query&format=json&formatversion=2&redirects=1&prop=extracts&exintro=1&explaintext=1&exlimit=20&titles="
                + Uri.EscapeDataString(string.Join('|', chunk));
            using var doc = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken).ConfigureAwait(false);
            if (doc is null)
            {
                return null;
            }

            var query = doc.RootElement.GetProperty("query");

            // Titles Wikipedia normalised or redirected, back to the ones asked for
            var renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var list in new[] { "normalized", "redirects" })
            {
                if (query.TryGetProperty(list, out var pairs))
                {
                    foreach (var pair in pairs.EnumerateArray())
                    {
                        var from = pair.GetProperty("from").GetString()!;
                        renamed[pair.GetProperty("to").GetString()!] = renamed.TryGetValue(from, out var original) ? original : from;
                    }
                }
            }

            if (!query.TryGetProperty("pages", out var pages))
            {
                continue;
            }

            foreach (var page in pages.EnumerateArray())
            {
                var title = page.TryGetProperty("title", out var t) ? t.GetString() : null;
                var extract = page.TryGetProperty("extract", out var e) ? e.GetString()?.Trim() : null;
                if (title is not null && !string.IsNullOrEmpty(extract))
                {
                    bios[renamed.TryGetValue(title, out var asked) ? asked : title] = extract;
                }
            }
        }

        return bios;
    }

    // Within Wikimedia's limits; null when it's busy, slow or unreachable (tried again later)
    private async Task<JsonDocument?> SendAsync(Func<HttpRequestMessage> makeRequest, CancellationToken cancellationToken)
    {
        if (!await SourceRateLimit.WaitTurnAsync(SourceRateLimit.Wikimedia, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(TimeSpan.FromSeconds(20));
            using var request = makeRequest();
            request.Headers.UserAgent.Clear();
            request.Headers.UserAgent.ParseAdd(UserAgent);
            request.Headers.Accept.ParseAdd("application/sparql-results+json, application/json");
            using var response = await _httpClientFactory.CreateClient(NamedClient.Default).SendAsync(request, limit.Token).ConfigureAwait(false);
            if (SourceRateLimit.CheckForPause(SourceRateLimit.Wikimedia, response) || !response.IsSuccessStatusCode)
            {
                return null;
            }

            var stream = await response.Content.ReadAsStreamAsync(limit.Token).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                return await JsonDocument.ParseAsync(stream, cancellationToken: limit.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Value(JsonElement row, string name)
        => row.TryGetProperty(name, out var cell) && cell.TryGetProperty("value", out var value) ? value.GetString() : null;

    private sealed class ArtistFacts
    {
        public string? MusicBrainzId { get; set; }

        public string? WikiTitle { get; set; }

        public string? Bio { get; set; }

        public string? Image { get; set; }
    }
}
