using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Khylo.Controller.Entities;
using Khylo.Controller.Library;
using Khylo.Controller.Listening;

namespace Khylo.Server.Core.Listening;

/// <summary>
/// The server owner's view of what is played and watched. It reads plain activity (who played what, when, how, on what), and has
/// no access to anyone's weekly, quarterly or yearly review: those are built only for the person who asks for their own.
/// </summary>
public class ListeningInsights : IListeningInsights
{
    private static readonly string[] AllKinds = ["music", "audiobook", "podcast", "movie", "episode"];
    private static readonly string[] AudioKinds = ["music", "audiobook", "podcast"];

    private readonly ListeningStore _store;
    private readonly InsightsStore _samples;
    private readonly IUserManager _users;
    private readonly ILibraryManager _library;
    private (DateTime At, long Bytes) _sizeCache;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListeningInsights"/> class.
    /// </summary>
    /// <param name="store">The history.</param>
    /// <param name="samples">The server readings.</param>
    /// <param name="users">The user manager.</param>
    /// <param name="library">The library manager.</param>
    public ListeningInsights(ListeningStore store, InsightsStore samples, IUserManager users, ILibraryManager library)
    {
        _store = store;
        _samples = samples;
        _users = users;
        _library = library;
    }

    /// <inheritdoc />
    public object Overview(int days, string? timeZone, string? kind)
    {
        var zone = ListeningStats.Zone(timeZone);
        var (rows, from) = Range(days, kind);
        var counted = rows.Where(r => r.Counted).ToList();
        var video = counted.Where(r => r.Kind is "movie" or "episode").ToList();
        var audio = counted.Where(r => r.Kind is not ("movie" or "episode")).ToList();
        var local = rows.Select(r => (Play: r, At: TimeZoneInfo.ConvertTimeFromUtc(r.StartedAt, zone))).ToList();

        return new Dictionary<string, object?>
        {
            ["TotalSeconds"] = Math.Round(rows.Sum(r => r.SecondsListened)),
            ["Views"] = counted.Count,
            ["ActiveUsers"] = rows.Select(r => r.UserId).Distinct().Count(),
            ["TopMovies"] = video.Where(r => r.Kind == "movie").GroupBy(r => r.MovieKey).OrderByDescending(g => g.Count()).Take(10)
                .Select(g => Item(g.First().Title, g)).ToList(),
            ["TopShows"] = video.Where(r => r.Kind == "episode").GroupBy(r => r.SeriesKey).OrderByDescending(g => g.Count()).Take(10)
                .Select(g => Item(g.First().SeriesName, g)).ToList(),
            ["TopArtists"] = audio.Where(r => r.Kind == "music").GroupBy(ListeningStats.ArtistKey).OrderByDescending(g => g.Count()).Take(10)
                .Select(g => new Dictionary<string, object?>
                {
                    ["ArtistId"] = ListeningStats.ArtistId(g.First()),
                    ["Name"] = ListeningStats.ArtistName(g.First()),
                    ["Plays"] = g.Count(),
                    ["Listeners"] = g.Select(r => r.UserId).Distinct().Count(),
                    ["Seconds"] = Math.Round(g.Sum(r => r.SecondsListened))
                }).ToList(),
            ["TopSongs"] = audio.Where(r => r.Kind == "music").GroupBy(r => r.SongKey).OrderByDescending(g => g.Count()).Take(10)
                .Select(g => new Dictionary<string, object?>
                {
                    ["Title"] = g.First().Title,
                    ["Artist"] = ListeningStats.ArtistName(g.First()),
                    ["Plays"] = g.Count(),
                    ["Listeners"] = g.Select(r => r.UserId).Distinct().Count()
                }).ToList(),
            ["TopAlbums"] = audio.Where(r => r.Kind == "music" && !string.IsNullOrEmpty(r.Album)).GroupBy(r => r.AlbumKey).OrderByDescending(g => g.Count()).Take(5)
                .Select(g => new Dictionary<string, object?>
                {
                    ["Name"] = g.First().Album,
                    ["Artist"] = g.First().AlbumArtist,
                    ["Plays"] = g.Count(),
                    ["Listeners"] = g.Select(r => r.UserId).Distinct().Count()
                }).ToList(),
            ["TopUsers"] = rows.GroupBy(r => r.UserId).OrderByDescending(g => g.Sum(r => r.SecondsListened)).Take(10)
                .Select(g => new Dictionary<string, object?>
                {
                    ["UserId"] = g.Key.ToString("N"),
                    ["Name"] = UserName(g.Key),
                    ["Seconds"] = Math.Round(g.Sum(r => r.SecondsListened)),
                    ["Views"] = g.Count(r => r.Counted),
                    ["LastSeen"] = g.Max(r => r.StartedAt)
                }).ToList(),
            ["ByDay"] = ByDay(local, from, zone),
            ["ByWeekday"] = Enumerable.Range(1, 7).Select(w => new Dictionary<string, object?>
            {
                ["Weekday"] = w,
                ["Seconds"] = Math.Round(local.Where(x => (x.At.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)x.At.DayOfWeek) == w).Sum(x => x.Play.SecondsListened))
            }).ToList(),
            ["ByHour"] = Enumerable.Range(0, 24).Select(h => new Dictionary<string, object?>
            {
                ["Hour"] = h,
                ["Seconds"] = Math.Round(local.Where(x => x.At.Hour == h).Sum(x => x.Play.SecondsListened))
            }).ToList(),
            ["ByPlatform"] = rows.GroupBy(r => r.Client ?? "Unknown").OrderByDescending(g => g.Count(r => r.Counted))
                .Select(g => new Dictionary<string, object?> { ["Client"] = g.Key, ["Views"] = g.Count(r => r.Counted), ["Seconds"] = Math.Round(g.Sum(r => r.SecondsListened)) }).ToList(),
            ["ByPlayMethod"] = new Dictionary<string, object?>
            {
                ["DirectPlay"] = video.Count(r => r.PlayMethod == "DirectPlay"),
                ["DirectStream"] = video.Count(r => r.PlayMethod == "DirectStream"),
                ["Transcode"] = video.Count(r => r.PlayMethod == "Transcode")
            },
            ["TopTranscodeReasons"] = video.Where(r => !string.IsNullOrEmpty(r.TranscodeReasons))
                .SelectMany(r => r.TranscodeReasons!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .GroupBy(x => x).OrderByDescending(g => g.Count()).Take(10)
                .Select(g => new Dictionary<string, object?> { ["Reason"] = g.Key, ["Views"] = g.Count() }).ToList(),
            ["ByResolution"] = video.Where(r => !string.IsNullOrEmpty(r.Resolution)).GroupBy(r => r.Resolution)
                .OrderByDescending(g => g.Count())
                .Select(g => new Dictionary<string, object?> { ["Resolution"] = g.Key, ["Views"] = g.Count() }).ToList(),
            ["ByLibrary"] = rows.Where(r => r.LibraryId is not null).GroupBy(r => r.LibraryId)
                .OrderByDescending(g => g.Sum(r => r.SecondsListened))
                .Select(g => new Dictionary<string, object?>
                {
                    ["LibraryId"] = g.Key?.ToString("N"),
                    ["Name"] = _library.GetItemById(g.Key!.Value)?.Name ?? string.Empty,
                    ["Seconds"] = Math.Round(g.Sum(r => r.SecondsListened)),
                    ["Views"] = g.Count(r => r.Counted)
                }).ToList(),
            ["Peak"] = Peak(rows)
        };
    }

    /// <inheritdoc />
    public object UserActivity(Guid userId, int days)
    {
        var from = days <= 0 ? DateTime.MinValue.AddYears(1) : DateTime.UtcNow.AddDays(-days);
        var rows = _store.Load(userId, from, DateTime.UtcNow.AddMinutes(1), AllKinds);
        var counted = rows.Where(r => r.Counted).ToList();
        return new Dictionary<string, object?>
        {
            ["UserId"] = userId.ToString("N"),
            ["Name"] = UserName(userId),
            ["Seconds"] = Math.Round(rows.Sum(r => r.SecondsListened)),
            ["Views"] = counted.Count,
            ["TopShows"] = counted.Where(r => r.Kind == "episode").GroupBy(r => r.SeriesKey).OrderByDescending(g => g.Count()).Take(5)
                .Select(g => new Dictionary<string, object?> { ["Title"] = g.First().SeriesName, ["Views"] = g.Count() }).ToList(),
            ["TopMovies"] = counted.Where(r => r.Kind == "movie").GroupBy(r => r.MovieKey).OrderByDescending(g => g.Count()).Take(5)
                .Select(g => new Dictionary<string, object?> { ["Title"] = g.First().Title, ["Views"] = g.Count() }).ToList(),
            ["TopArtists"] = counted.Where(r => r.Kind == "music").GroupBy(ListeningStats.ArtistKey).OrderByDescending(g => g.Count()).Take(5)
                .Select(g => new Dictionary<string, object?> { ["Name"] = ListeningStats.ArtistName(g.First()), ["Plays"] = g.Count() }).ToList(),
            ["ByPlatform"] = rows.GroupBy(r => r.Client ?? "Unknown")
                .Select(g => new Dictionary<string, object?> { ["Client"] = g.Key, ["Views"] = g.Count(r => r.Counted), ["Seconds"] = Math.Round(g.Sum(r => r.SecondsListened)) }).ToList(),
            ["Recent"] = rows.OrderByDescending(r => r.StartedAt).Take(25).Select(HistoryRow).ToList()
        };
    }

    /// <inheritdoc />
    public object History(DateTime? before, int limit, Guid? userId, string? kind)
    {
        var rows = _store.LoadBefore(userId, before?.ToUniversalTime() ?? DateTime.UtcNow.AddMinutes(1), limit, Kinds(kind));
        return new Dictionary<string, object?> { ["Items"] = rows.Select(HistoryRow).ToList(), ["Next"] = rows.Count == 0 ? null : rows[^1].StartedAt };
    }

    /// <inheritdoc />
    public object Resources(DateTime? from, DateTime? to, int stepMinutes)
        => _samples.Points(from ?? DateTime.UtcNow.AddDays(-1), to ?? DateTime.UtcNow, stepMinutes <= 0 ? 60 : stepMinutes);

    /// <inheritdoc />
    public object Library()
    {
        var libraries = _library.GetVirtualFolders().Select(f => new Dictionary<string, object?>
        {
            ["LibraryId"] = f.ItemId,
            ["Name"] = f.Name,
            ["Items"] = Guid.TryParse(f.ItemId, out var id)
                ? _library.GetCount(new InternalItemsQuery { ParentId = id, Recursive = true, IsFolder = false })
                : 0
        }).ToList();

        var weeks = new List<Dictionary<string, object?>>();
        var monday = DateTime.UtcNow.Date.AddDays(-(((int)DateTime.UtcNow.DayOfWeek + 6) % 7));
        var since = Enumerable.Range(0, 13).Select(w => monday.AddDays(-7 * w)).ToList();
        var counts = since.Select(d => _library.GetCount(new InternalItemsQuery { Recursive = true, IsFolder = false, MinDateCreated = d })).ToList();
        for (var w = 11; w >= 0; w--)
        {
            weeks.Add(new Dictionary<string, object?>
            {
                ["WeekOf"] = since[w].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["Added"] = counts[w] - counts[w + 1]
            });
        }

        return new Dictionary<string, object?> { ["Libraries"] = libraries, ["AddedPerWeek"] = weeks, ["TotalBytes"] = TotalSize() };
    }

    private static string[] Kinds(string? kind) => kind?.ToLowerInvariant() switch
    {
        "video" => ["movie", "episode"],
        "audio" => AudioKinds,
        _ => AllKinds
    };

    private static Dictionary<string, object?> Item(string? title, IEnumerable<ListeningPlay> group)
    {
        var plays = group.ToList();
        return new Dictionary<string, object?>
        {
            ["Title"] = title,
            ["Views"] = plays.Count,
            ["Users"] = plays.Select(r => r.UserId).Distinct().Count(),
            ["Seconds"] = Math.Round(plays.Sum(r => r.SecondsListened))
        };
    }

    private static Dictionary<string, object?>? Peak(IReadOnlyList<ListeningPlay> rows)
    {
        var events = rows
            .SelectMany(r => new[] { (At: r.StartedAt, Change: 1), (At: r.StartedAt.AddSeconds(Math.Max(1, r.SecondsListened)), Change: -1) })
            .OrderBy(e => e.At).ThenBy(e => e.Change)
            .ToList();
        int now = 0, best = 0;
        var at = DateTime.MinValue;
        foreach (var e in events)
        {
            now += e.Change;
            if (now > best)
            {
                best = now;
                at = e.At;
            }
        }

        return best == 0 ? null : new Dictionary<string, object?> { ["Streams"] = best, ["At"] = at };
    }

    private static List<Dictionary<string, object?>> ByDay(List<(ListeningPlay Play, DateTime At)> local, DateTime from, TimeZoneInfo zone)
    {
        var first = local.Count == 0 ? DateTime.UtcNow.Date : local.Min(x => x.At).Date;
        var start = from > DateTime.MinValue.AddYears(2) ? TimeZoneInfo.ConvertTimeFromUtc(from, zone).Date : first;
        var end = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone).Date;
        var byDay = local.GroupBy(x => x.At.Date).ToDictionary(g => g.Key);
        var list = new List<Dictionary<string, object?>>();
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            byDay.TryGetValue(day, out var group);
            list.Add(new Dictionary<string, object?>
            {
                ["Date"] = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["Seconds"] = Math.Round(group?.Sum(x => x.Play.SecondsListened) ?? 0),
                ["Views"] = group?.Count(x => x.Play.Counted) ?? 0
            });
        }

        return list;
    }

    private (IReadOnlyList<ListeningPlay> Rows, DateTime From) Range(int days, string? kind)
    {
        var from = days <= 0 ? DateTime.MinValue.AddYears(1) : DateTime.UtcNow.AddDays(-days);
        return (_store.Load(null, from, DateTime.UtcNow.AddMinutes(1), Kinds(kind)), from);
    }

    private Dictionary<string, object?> HistoryRow(ListeningPlay r) => new()
    {
        ["StartedAt"] = r.StartedAt,
        ["UserId"] = r.UserId.ToString("N"),
        ["User"] = UserName(r.UserId),
        ["Kind"] = r.Kind,
        ["Title"] = r.SeriesName is { Length: > 0 } show ? show + " - " + r.Title : r.Title,
        ["SecondsListened"] = Math.Round(r.SecondsListened),
        ["PlayMethod"] = r.PlayMethod,
        ["Client"] = r.Client,
        ["Resolution"] = r.Resolution,
        ["IsLocal"] = r.IsLocal
    };

    private string UserName(Guid id) => _users.GetUserById(id)?.Username ?? "Removed user";

    private long TotalSize()
    {
        if (DateTime.UtcNow - _sizeCache.At < TimeSpan.FromMinutes(10))
        {
            return _sizeCache.Bytes;
        }

        var total = _library.GetItemList(new InternalItemsQuery { Recursive = true, IsFolder = false }).Sum(i => i.Size ?? 0);
        _sizeCache = (DateTime.UtcNow, total);
        return total;
    }
}
