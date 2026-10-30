using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Khylo.Api.Helpers.KhyloImport;

/// <summary>
/// Reads watch and listening history from a Plex Media Server with a Plex token. The owner's token
/// also lists the people in their Plex Home, who can each be imported (with their PIN when they have one).
/// </summary>
public sealed class PlexImportSource
{
    private const int PageSize = 1000;
    private const string PlexTv = "https://plex.tv";
    private const string SelfId = "self";

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _token;
    private readonly string _clientId;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlexImportSource"/> class.
    /// </summary>
    /// <param name="http">The HTTP client.</param>
    /// <param name="baseUrl">The Plex server's address.</param>
    /// <param name="token">The owner's (or a user's) Plex token.</param>
    /// <param name="clientId">An id for Khylo as a Plex client.</param>
    public PlexImportSource(HttpClient http, string baseUrl, string token, string clientId)
    {
        _http = http;
        _baseUrl = ImportHttp.NormalizeUrl(baseUrl);
        _token = token.Trim();
        _clientId = clientId;
    }

    /// <summary>
    /// Gets the account the token belongs to, plus everyone in its Plex Home.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The users.</returns>
    public async Task<List<ImportSourceUser>> GetUsersAsync(CancellationToken cancellationToken)
    {
        // Proves the server and token work before anything else
        using (await GetServerJsonAsync("/library/sections", _token, cancellationToken).ConfigureAwait(false))
        {
        }

        try
        {
            var xml = await GetPlexTvXmlAsync("/api/home/users", _token, cancellationToken).ConfigureAwait(false);
            var users = xml.Descendants("User")
                .Select(u => new ImportSourceUser
                {
                    Id = string.Equals((string?)u.Attribute("admin"), "1", StringComparison.Ordinal) ? SelfId : "home:" + (string?)u.Attribute("id"),
                    Name = (string?)u.Attribute("title") ?? (string?)u.Attribute("username") ?? "Plex user",
                    NeedsPin = string.Equals((string?)u.Attribute("protected"), "1", StringComparison.Ordinal)
                        && !string.Equals((string?)u.Attribute("admin"), "1", StringComparison.Ordinal)
                })
                .ToList();
            if (users.Count > 0)
            {
                return users;
            }
        }
        catch (ImportException)
        {
            // Not a Plex Home, or plex.tv can't be reached: just the token's own account
        }

        return [new ImportSourceUser { Id = SelfId, Name = "Plex account" }];
    }

    /// <summary>
    /// Gets everything one person has watched, started, played or rated.
    /// </summary>
    /// <param name="userId">The user from <see cref="GetUsersAsync"/>.</param>
    /// <param name="pin">Their Plex Home PIN, if they have one.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Their history.</returns>
    public async Task<List<ImportEntry>> GetEntriesAsync(string userId, string? pin, CancellationToken cancellationToken)
    {
        var token = await GetServerTokenForAsync(userId, pin, cancellationToken).ConfigureAwait(false);
        var entries = new List<ImportEntry>();

        using var sectionsDoc = await GetServerJsonAsync("/library/sections", token, cancellationToken).ConfigureAwait(false);
        var sections = Container(sectionsDoc.RootElement, "Directory")
            .Select(d => (Key: ImportHttp.Str(d, "key"), Type: ImportHttp.Str(d, "type")))
            .Where(s => s.Key is not null)
            .ToList();

        foreach (var (key, type) in sections)
        {
            switch (type)
            {
                case "movie":
                    await foreach (var m in PageAsync($"/library/sections/{key}/all?type=1&includeGuids=1", token, cancellationToken).ConfigureAwait(false))
                    {
                        AddIfHasHistory(entries, Base(m, ImportEntryKind.Movie));
                    }

                    break;

                case "show":
                    var shows = new Dictionary<string, (string Title, int? Year, Dictionary<string, string> Ids)>(StringComparer.Ordinal);
                    await foreach (var s in PageAsync($"/library/sections/{key}/all?type=2&includeGuids=1", token, cancellationToken).ConfigureAwait(false))
                    {
                        if (ImportHttp.Str(s, "ratingKey") is { } ratingKey)
                        {
                            shows[ratingKey] = (ImportHttp.Str(s, "title") ?? string.Empty, ImportHttp.Int(s, "year"), Guids(s));
                        }

                        // A rated show counts as a favorite show
                        var show = Base(s, ImportEntryKind.Series);
                        show.Played = false;
                        show.PlayCount = 0;
                        show.PositionTicks = 0;
                        if (show.Favorite)
                        {
                            entries.Add(show);
                        }
                    }

                    await foreach (var e in PageAsync($"/library/sections/{key}/all?type=4&includeGuids=1", token, cancellationToken).ConfigureAwait(false))
                    {
                        var entry = Base(e, ImportEntryKind.Episode);
                        entry.SeriesTitle = ImportHttp.Str(e, "grandparentTitle");
                        entry.Season = ImportHttp.Int(e, "parentIndex");
                        entry.Episode = ImportHttp.Int(e, "index");
                        if (ImportHttp.Str(e, "grandparentRatingKey") is { } showKey && shows.TryGetValue(showKey, out var showInfo))
                        {
                            entry.SeriesIds = showInfo.Ids;
                            entry.SeriesYear = showInfo.Year;
                            entry.SeriesTitle ??= showInfo.Title;
                        }

                        AddIfHasHistory(entries, entry);
                    }

                    break;

                case "artist":
                    await foreach (var t in PageAsync($"/library/sections/{key}/all?type=10&includeGuids=1", token, cancellationToken).ConfigureAwait(false))
                    {
                        var entry = Base(t, ImportEntryKind.Track);
                        entry.Album = ImportHttp.Str(t, "parentTitle");
                        entry.Artist = ImportHttp.Str(t, "grandparentTitle") ?? ImportHttp.Str(t, "originalTitle");
                        if (entry.Ids.Remove("mbid", out var mbid))
                        {
                            entry.Ids["mbrecording"] = mbid;
                        }

                        AddIfHasHistory(entries, entry);
                    }

                    break;
            }
        }

        return entries;
    }

    private static void AddIfHasHistory(List<ImportEntry> entries, ImportEntry entry)
    {
        if (entry.Played || entry.PositionTicks > 0 || entry.Favorite || entry.Rating is > 0)
        {
            entries.Add(entry);
        }
    }

    private static ImportEntry Base(JsonElement m, ImportEntryKind kind)
    {
        var viewCount = ImportHttp.Int(m, "viewCount") ?? 0;
        var rating = m.TryGetProperty("userRating", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetDouble() : (double?)null;
        var entry = new ImportEntry
        {
            Kind = kind,
            Title = ImportHttp.Str(m, "title") ?? string.Empty,
            Year = ImportHttp.Int(m, "year"),
            Ids = Guids(m),
            RunTimeTicks = ImportHttp.Long(m, "duration") * TimeSpan.TicksPerMillisecond,
            Played = viewCount > 0,
            PlayCount = viewCount,
            PositionTicks = (ImportHttp.Long(m, "viewOffset") ?? 0) * TimeSpan.TicksPerMillisecond,
            Rating = rating,

            // Plex has no favorites; a full five stars (or a "love" in Plexamp) is the closest thing
            Favorite = rating >= 10
        };

        if (ImportHttp.Long(m, "lastViewedAt") is { } seconds and > 0)
        {
            entry.LastPlayed = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
        }

        if (m.TryGetProperty("Media", out var media) && media.ValueKind == JsonValueKind.Array)
        {
            entry.Path = media.EnumerateArray()
                .SelectMany(x => x.TryGetProperty("Part", out var parts) && parts.ValueKind == JsonValueKind.Array ? parts.EnumerateArray() : Enumerable.Empty<JsonElement>())
                .Select(p => ImportHttp.Str(p, "file"))
                .FirstOrDefault(f => !string.IsNullOrEmpty(f));
        }

        return entry;
    }

    // "imdb://tt0111161", "tmdb://278", "tvdb://81189", "mbid://…"
    private static Dictionary<string, string> Guids(JsonElement m)
    {
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (m.TryGetProperty("Guid", out var guids) && guids.ValueKind == JsonValueKind.Array)
        {
            foreach (var g in guids.EnumerateArray())
            {
                var id = ImportHttp.Str(g, "id");
                var split = id?.IndexOf("://", StringComparison.Ordinal) ?? -1;
                if (id is not null && split > 0)
                {
                    ids[id[..split]] = id[(split + 3)..];
                }
            }
        }

        return ids;
    }

    private static IEnumerable<JsonElement> Container(JsonElement root, string name)
        => root.TryGetProperty("MediaContainer", out var mc) && mc.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray()
            : Enumerable.Empty<JsonElement>();

    private async IAsyncEnumerable<JsonElement> PageAsync(string path, string token, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var start = 0; ; start += PageSize)
        {
            var separator = path.Contains('?', StringComparison.Ordinal) ? '&' : '?';
            using var doc = await GetServerJsonAsync($"{path}{separator}X-Plex-Container-Start={start}&X-Plex-Container-Size={PageSize}", token, cancellationToken).ConfigureAwait(false);
            var items = Container(doc.RootElement, "Metadata").Select(e => e.Clone()).ToList();
            foreach (var item in items)
            {
                yield return item;
            }

            if (items.Count < PageSize)
            {
                yield break;
            }
        }
    }

    private async Task<string> GetServerTokenForAsync(string userId, string? pin, CancellationToken cancellationToken)
    {
        if (!userId.StartsWith("home:", StringComparison.Ordinal))
        {
            return _token;
        }

        // Switch to the Plex Home user, then find their own token for this server
        var path = $"/api/home/users/{userId[5..]}/switch" + (string.IsNullOrWhiteSpace(pin) ? string.Empty : "?pin=" + Uri.EscapeDataString(pin.Trim()));
        XDocument switched;
        try
        {
            switched = await SendPlexTvXmlAsync(HttpMethod.Post, path, _token, cancellationToken).ConfigureAwait(false);
        }
        catch (ImportException)
        {
            throw new ImportException("Couldn't switch to this Plex Home user. If they have a PIN, check it.");
        }

        var userToken = switched.Root?.Attribute("authenticationToken")?.Value ?? switched.Root?.Attribute("authToken")?.Value;
        if (string.IsNullOrEmpty(userToken))
        {
            throw new ImportException("Plex didn't give a token for this Plex Home user.");
        }

        try
        {
            using var identity = await GetServerJsonAsync("/identity", _token, cancellationToken).ConfigureAwait(false);
            var machineId = identity.RootElement.TryGetProperty("MediaContainer", out var mc) ? ImportHttp.Str(mc, "machineIdentifier") : null;

            using var request = new HttpRequestMessage(HttpMethod.Get, PlexTv + "/api/v2/resources?includeHttps=1");
            AddPlexHeaders(request, userToken);
            using var resources = await ImportHttp.SendForJsonAsync(_http, request, cancellationToken).ConfigureAwait(false);
            var serverToken = resources.RootElement.EnumerateArray()
                .Where(r => string.Equals(ImportHttp.Str(r, "clientIdentifier"), machineId, StringComparison.Ordinal))
                .Select(r => ImportHttp.Str(r, "accessToken"))
                .FirstOrDefault(t => !string.IsNullOrEmpty(t));
            return serverToken ?? userToken;
        }
        catch (ImportException)
        {
            return userToken;
        }
    }

    private void AddPlexHeaders(HttpRequestMessage request, string token)
    {
        request.Headers.TryAddWithoutValidation("X-Plex-Token", token);
        request.Headers.TryAddWithoutValidation("X-Plex-Client-Identifier", _clientId);
        request.Headers.TryAddWithoutValidation("X-Plex-Product", "Khylo");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
    }

    private async Task<JsonDocument> GetServerJsonAsync(string path, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _baseUrl + path);
        AddPlexHeaders(request, token);
        return await ImportHttp.SendForJsonAsync(_http, request, cancellationToken).ConfigureAwait(false);
    }

    private Task<XDocument> GetPlexTvXmlAsync(string path, string token, CancellationToken cancellationToken)
        => SendPlexTvXmlAsync(HttpMethod.Get, path, token, cancellationToken);

    private async Task<XDocument> SendPlexTvXmlAsync(HttpMethod method, string path, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, PlexTv + path);
        AddPlexHeaders(request, token);
        request.Headers.Remove("Accept");
        request.Headers.TryAddWithoutValidation("Accept", "application/xml");
        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new ImportException($"plex.tv answered {(int)response.StatusCode}.");
            }

            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return XDocument.Parse(text);
        }
        catch (HttpRequestException ex)
        {
            throw new ImportException("Couldn't reach plex.tv. " + ex.Message);
        }
        catch (System.Xml.XmlException)
        {
            throw new ImportException("plex.tv's answer wasn't what Khylo expected.");
        }
    }
}
