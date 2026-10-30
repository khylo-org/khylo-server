using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Khylo.Api.Helpers.KhyloImport;

/// <summary>
/// Reads watch history from a Khylo or Emby server with an admin API key.
/// </summary>
public sealed class KhyloImportSource
{
    private const int PageSize = 500;
    private const string PlayableTypes = "Movie,Episode,Audio,AudioBook,MusicVideo";
    private const string FavoriteOnlyTypes = "Series,MusicAlbum,MusicArtist";

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _token;

    /// <summary>
    /// Initializes a new instance of the <see cref="KhyloImportSource"/> class.
    /// </summary>
    /// <param name="http">The HTTP client.</param>
    /// <param name="baseUrl">The server's address.</param>
    /// <param name="token">An admin API key.</param>
    public KhyloImportSource(HttpClient http, string baseUrl, string token)
    {
        _http = http;
        _baseUrl = ImportHttp.NormalizeUrl(baseUrl);
        _token = token.Trim();
    }

    /// <summary>
    /// Gets everyone on the server.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The users.</returns>
    public async Task<List<ImportSourceUser>> GetUsersAsync(CancellationToken cancellationToken)
    {
        using var doc = await GetJsonAsync("/Users", cancellationToken).ConfigureAwait(false);
        return doc.RootElement.EnumerateArray()
            .Select(u => new ImportSourceUser { Id = ImportHttp.Str(u, "Id") ?? string.Empty, Name = ImportHttp.Str(u, "Name") ?? string.Empty })
            .Where(u => u.Id.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Gets everything one person has watched, started, played or favorited.
    /// </summary>
    /// <param name="userId">The user on the other server.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Their history.</returns>
    public async Task<List<ImportEntry>> GetEntriesAsync(string userId, CancellationToken cancellationToken)
    {
        // The same title can turn up under several filters; keep it once
        var byId = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        var docs = new List<JsonDocument>();
        try
        {
            foreach (var (filter, types) in new[] { ("IsPlayed", PlayableTypes), ("IsResumable", PlayableTypes), ("IsFavorite", PlayableTypes + "," + FavoriteOnlyTypes) })
            {
                for (var start = 0; ; start += PageSize)
                {
                    var doc = await GetJsonAsync(
                        $"/Users/{userId}/Items?Recursive=true&Filters={filter}&IncludeItemTypes={types}&Fields=ProviderIds,Path,ProductionYear&EnableUserData=true&EnableImages=false&StartIndex={start}&Limit={PageSize}",
                        cancellationToken).ConfigureAwait(false);
                    docs.Add(doc);
                    var items = doc.RootElement.TryGetProperty("Items", out var list) ? list.EnumerateArray().ToList() : [];
                    foreach (var item in items)
                    {
                        if (ImportHttp.Str(item, "Id") is { } id)
                        {
                            byId[id] = item;
                        }
                    }

                    if (items.Count < PageSize)
                    {
                        break;
                    }
                }
            }

            // The shows' own ids help match episodes
            var seriesIds = await GetSeriesIdsAsync(byId.Values.Select(i => ImportHttp.Str(i, "SeriesId")).OfType<string>().Distinct().ToList(), userId, cancellationToken).ConfigureAwait(false);

            return byId.Values.Select(i => ToEntry(i, seriesIds)).OfType<ImportEntry>().ToList();
        }
        finally
        {
            foreach (var doc in docs)
            {
                doc.Dispose();
            }
        }
    }

    private async Task<Dictionary<string, (Dictionary<string, string> Ids, int? Year)>> GetSeriesIdsAsync(List<string> ids, string userId, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, (Dictionary<string, string>, int?)>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in ids.Chunk(100))
        {
            using var doc = await GetJsonAsync($"/Users/{userId}/Items?Ids={string.Join(',', chunk)}&Fields=ProviderIds,ProductionYear&EnableImages=false", cancellationToken).ConfigureAwait(false);
            if (!doc.RootElement.TryGetProperty("Items", out var list))
            {
                continue;
            }

            foreach (var s in list.EnumerateArray())
            {
                if (ImportHttp.Str(s, "Id") is { } id)
                {
                    result[id] = (ProviderIds(s), ImportHttp.Int(s, "ProductionYear"));
                }
            }
        }

        return result;
    }

    private static ImportEntry? ToEntry(JsonElement item, Dictionary<string, (Dictionary<string, string> Ids, int? Year)> series)
    {
        var kind = ImportHttp.Str(item, "Type") switch
        {
            "Movie" => ImportEntryKind.Movie,
            "Episode" => ImportEntryKind.Episode,
            "Audio" => ImportEntryKind.Track,
            "AudioBook" => ImportEntryKind.AudioBook,
            "MusicVideo" => ImportEntryKind.MusicVideo,
            "Series" => ImportEntryKind.Series,
            "MusicAlbum" => ImportEntryKind.Album,
            "MusicArtist" => ImportEntryKind.Artist,
            _ => (ImportEntryKind?)null
        };
        if (kind is null)
        {
            return null;
        }

        var entry = new ImportEntry
        {
            Kind = kind.Value,
            Title = ImportHttp.Str(item, "Name") ?? string.Empty,
            Year = ImportHttp.Int(item, "ProductionYear"),
            Ids = ProviderIds(item),
            SeriesTitle = ImportHttp.Str(item, "SeriesName"),
            Season = ImportHttp.Int(item, "ParentIndexNumber"),
            Episode = ImportHttp.Int(item, "IndexNumber"),
            Album = ImportHttp.Str(item, "Album"),
            Artist = ImportHttp.Str(item, "AlbumArtist")
                ?? (item.TryGetProperty("Artists", out var artists) && artists.ValueKind == JsonValueKind.Array ? artists.EnumerateArray().Select(a => a.GetString()).FirstOrDefault() : null),
            RunTimeTicks = ImportHttp.Long(item, "RunTimeTicks"),
            Path = ImportHttp.Str(item, "Path")
        };

        if (ImportHttp.Str(item, "SeriesId") is { } seriesId && series.TryGetValue(seriesId, out var s))
        {
            entry.SeriesIds = s.Ids;
            entry.SeriesYear = s.Year;
        }

        if (item.TryGetProperty("UserData", out var data))
        {
            entry.Played = ImportHttp.Bool(data, "Played");
            entry.PlayCount = ImportHttp.Int(data, "PlayCount") ?? 0;
            entry.PositionTicks = ImportHttp.Long(data, "PlaybackPositionTicks") ?? 0;
            entry.Favorite = ImportHttp.Bool(data, "IsFavorite");
            entry.Rating = data.TryGetProperty("Rating", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetDouble() : null;
            if (ImportHttp.Str(data, "LastPlayedDate") is { } last && DateTime.TryParse(last, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var lastPlayed))
            {
                entry.LastPlayed = lastPlayed;
            }
        }

        return entry;
    }

    private static Dictionary<string, string> ProviderIds(JsonElement item)
    {
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!item.TryGetProperty("ProviderIds", out var providers) || providers.ValueKind != JsonValueKind.Object)
        {
            return ids;
        }

        foreach (var p in providers.EnumerateObject())
        {
            var value = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var key = p.Name.ToLowerInvariant() switch
            {
                "imdb" => "imdb",
                "tmdb" => "tmdb",
                "tvdb" => "tvdb",
                "musicbrainztrack" => "mbtrack",
                "musicbrainzrecording" => "mbrecording",
                "musicbrainzalbum" => "mbalbum",
                "musicbrainzreleasegroup" => "mbreleasegroup",
                "musicbrainzartist" => "mbartist",
                "musicbrainzalbumartist" => "mbalbumartist",
                _ => null
            };
            if (key is not null)
            {
                ids[key] = value.Trim();
            }
        }

        return ids;
    }

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _baseUrl + path);
        request.Headers.TryAddWithoutValidation("Authorization", $"MediaBrowser Token=\"{_token}\"");
        request.Headers.TryAddWithoutValidation("X-Emby-Token", _token);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        return await ImportHttp.SendForJsonAsync(_http, request, cancellationToken).ConfigureAwait(false);
    }
}
