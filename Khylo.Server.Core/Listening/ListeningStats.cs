using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Khylo.Server.Core.Listening;

/// <summary>
/// Turns recorded sessions into the numbers behind the weekly stats, the Wrapped and the quarterly reviews. Pure functions: they
/// read nothing but the sessions they are given, which are always one person's own.
/// </summary>
public static class ListeningStats
{
    /// <summary>The kinds counted as music.</summary>
    public static readonly IReadOnlyCollection<string> MusicKinds = ["music"];

    /// <summary>The kinds counted as video.</summary>
    public static readonly IReadOnlyCollection<string> VideoKinds = ["movie", "episode"];

    /// <summary>
    /// Finds a time zone by name, falling back to UTC.
    /// </summary>
    /// <param name="name">An IANA name such as America/Los_Angeles.</param>
    /// <returns>The zone.</returns>
    public static TimeZoneInfo Zone(string? name)
    {
        try
        {
            return string.IsNullOrWhiteSpace(name) ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(name);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    /// <summary>
    /// The start of a local day as a UTC moment.
    /// </summary>
    /// <param name="day">The local day.</param>
    /// <param name="zone">The zone.</param>
    /// <returns>The UTC moment the day begins.</returns>
    public static DateTime StartOfDay(DateOnly day, TimeZoneInfo zone)
        => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), zone);

    /// <summary>
    /// When a year's Wrapped opens: the Monday after Black Friday (Thanksgiving is the fourth Thursday of November).
    /// </summary>
    /// <param name="year">The year.</param>
    /// <returns>The opening day.</returns>
    public static DateOnly WrappedOpens(int year)
    {
        var day = new DateOnly(year, 11, 1);
        while (day.DayOfWeek != DayOfWeek.Thursday)
        {
            day = day.AddDays(1);
        }

        return day.AddDays(21 + 4);
    }

    /// <summary>
    /// When a quarter's review opens: the first Monday after the quarter ends.
    /// </summary>
    /// <param name="year">The year.</param>
    /// <param name="quarter">1 to 4.</param>
    /// <returns>The opening day.</returns>
    public static DateOnly QuarterOpens(int year, int quarter)
    {
        var day = QuarterEnd(year, quarter).AddDays(1);
        while (day.DayOfWeek != DayOfWeek.Monday)
        {
            day = day.AddDays(1);
        }

        return day;
    }

    /// <summary>
    /// The first day of a quarter.
    /// </summary>
    /// <param name="year">The year.</param>
    /// <param name="quarter">1 to 4.</param>
    /// <returns>The day.</returns>
    public static DateOnly QuarterStart(int year, int quarter) => new(year, ((quarter - 1) * 3) + 1, 1);

    /// <summary>
    /// The last day of a quarter.
    /// </summary>
    /// <param name="year">The year.</param>
    /// <param name="quarter">1 to 4.</param>
    /// <returns>The day.</returns>
    public static DateOnly QuarterEnd(int year, int quarter) => QuarterStart(year, quarter).AddMonths(3).AddDays(-1);

    /// <summary>
    /// The summary of a week, month or year of music or video.
    /// </summary>
    /// <param name="video">True for movies and shows.</param>
    /// <param name="rows">The sessions in the range.</param>
    /// <param name="earlier">Everything the person played before the range.</param>
    /// <param name="previous">The sessions in the range of the same length just before.</param>
    /// <param name="from">The first local day.</param>
    /// <param name="to">The last local day (inclusive).</param>
    /// <param name="zone">The person's time zone.</param>
    /// <param name="resolve">Finds the newest copy of a song that is still in the library.</param>
    /// <param name="actors">Names the leading cast of what was watched, for the top actors.</param>
    /// <returns>The summary.</returns>
    public static Dictionary<string, object?> Summary(bool video, IReadOnlyList<ListeningPlay> rows, IReadOnlyList<ListeningPlay> earlier, IReadOnlyList<ListeningPlay> previous, DateOnly from, DateOnly to, TimeZoneInfo zone, Func<IEnumerable<Guid>, Guid?> resolve, Func<ListeningPlay, IReadOnlyList<string>>? actors = null)
    {
        var days = Days(rows, from, to, zone);
        var summary = Totals(video, rows);
        summary["From"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        summary["To"] = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        summary["Days"] = days;
        AddTops(video, summary, rows, earlier, resolve, actors);
        summary["BusiestDay"] = BusiestDay(days);
        summary["Previous"] = new Dictionary<string, object?>
        {
            ["TotalSeconds"] = Math.Round(previous.Sum(r => r.SecondsListened)),
            [video ? "Views" : "Plays"] = previous.Count(r => r.Counted)
        };
        return summary;
    }

    /// <summary>
    /// The year-in-review (Wrapped) of a stretch of time, also used for a quarter.
    /// </summary>
    /// <param name="video">True for movies and shows.</param>
    /// <param name="rows">The sessions in the range.</param>
    /// <param name="earlier">Everything the person played before the range.</param>
    /// <param name="from">The first local day.</param>
    /// <param name="to">The last local day (inclusive).</param>
    /// <param name="zone">The person's time zone.</param>
    /// <param name="resolve">Finds the newest copy of a song that is still in the library.</param>
    /// <param name="actors">Names the leading cast of what was watched, for the top actors.</param>
    /// <param name="episodeCount">Says how many episodes a show has in the library, to tell which shows were finished.</param>
    /// <returns>The review.</returns>
    public static Dictionary<string, object?> Review(bool video, IReadOnlyList<ListeningPlay> rows, IReadOnlyList<ListeningPlay> earlier, DateOnly from, DateOnly to, TimeZoneInfo zone, Func<IEnumerable<Guid>, Guid?> resolve, Func<ListeningPlay, IReadOnlyList<string>>? actors = null, Func<Guid, int>? episodeCount = null)
    {
        var days = Days(rows, from, to, zone);
        var review = Totals(video, rows);
        review["From"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        review["To"] = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        AddTops(video, review, rows, earlier, resolve, actors);
        var busiest = days.OrderByDescending(d => (double)d["Seconds"]!).FirstOrDefault();
        review["BusiestDay"] = busiest is null || (double)busiest["Seconds"]! <= 0
            ? null
            : new Dictionary<string, object?>
            {
                ["Date"] = busiest["Date"],
                ["Seconds"] = busiest["Seconds"],
                ["TopArtist"] = TopNameOnDay(video, rows, zone, (string)busiest["Date"]!)
            };
        review["LongestStreakDays"] = LongestStreak(rows, zone);
        review["ByMonth"] = ByMonth(video, rows, from, to, zone);
        review["ByWeekday"] = Enumerable.Range(1, 7).Select(w => new Dictionary<string, object?>
        {
            ["Weekday"] = w,
            ["Seconds"] = Math.Round(rows.Where(r => WeekdayNumber(Local(r, zone)) == w).Sum(r => r.SecondsListened))
        }).ToList();
        var hours = Enumerable.Range(0, 24).Select(h => new Dictionary<string, object?>
        {
            ["Hour"] = h,
            ["Seconds"] = Math.Round(rows.Where(r => Local(r, zone).Hour == h).Sum(r => r.SecondsListened))
        }).ToList();
        review["ByHour"] = hours;
        review["TimeOfDay"] = TimeOfDay(hours);
        var ordered = rows.OrderBy(r => r.StartedAt).ToList();
        review["First" + (video ? "Watch" : "Song")] = ordered.Count == 0 ? null : Pick(ordered[0], zone);
        review["Last" + (video ? "Watch" : "Song")] = ordered.Count == 0 ? null : Pick(ordered[^1], zone);
        review[video ? "MostRewatched" : "TopSongPlays"] = video ? MostRewatched(rows) : TopSongPlays(rows);
        if (video)
        {
            review["LongestBinge"] = LongestBinge(rows, zone);
            review["ShowsCompleted"] = ShowsCompleted(rows, earlier, episodeCount);
        }

        return review;
    }

    /// <summary>
    /// How much one set of sessions changed against the one before it, for a quarterly review.
    /// </summary>
    /// <param name="video">True for movies and shows.</param>
    /// <param name="review">The review to add to.</param>
    /// <param name="rows">The quarter's sessions.</param>
    /// <param name="previousRows">The quarter before it.</param>
    public static void AddComparison(bool video, Dictionary<string, object?> review, IReadOnlyList<ListeningPlay> rows, IReadOnlyList<ListeningPlay> previousRows)
    {
        var total = rows.Sum(r => r.SecondsListened);
        var before = previousRows.Sum(r => r.SecondsListened);
        review["Previous"] = new Dictionary<string, object?>
        {
            ["TotalSeconds"] = Math.Round(before),
            [video ? "Views" : "Plays"] = previousRows.Count(r => r.Counted)
        };
        review["ChangePercent"] = before <= 0 ? null : (double?)Math.Round((total - before) / before * 100, 1);

        var now = Ranking(video, rows);
        var then = Ranking(video, previousRows);
        review["TopChanged"] = now.Count > 0 && (then.Count == 0 || now[0].Key != then[0].Key);
        review["Climbers"] = now
            .Select((entry, index) => (entry, index, was: then.FindIndex(t => t.Key == entry.Key)))
            .Where(x => x.was < 0 ? x.index < 10 && then.Count > 0 : x.was > x.index)
            .OrderByDescending(x => x.was < 0 ? then.Count : x.was - x.index)
            .Take(3)
            .Select(x => new Dictionary<string, object?> { ["Name"] = x.entry.Name, ["From"] = x.was < 0 ? null : x.was + 1, ["To"] = x.index + 1 })
            .ToList();
    }

    /// <summary>
    /// The first artist of a session, falling back to the album artist.
    /// </summary>
    /// <param name="play">The session.</param>
    /// <returns>The artist's name.</returns>
    public static string ArtistName(ListeningPlay play) => Names(play.ArtistsJson).FirstOrDefault() ?? play.AlbumArtist ?? string.Empty;

    /// <summary>
    /// The library id of the first artist of a session, when it is known.
    /// </summary>
    /// <param name="play">The session.</param>
    /// <returns>The artist's id, or null.</returns>
    public static string? ArtistId(ListeningPlay play) => Names(play.ArtistIdsJson).FirstOrDefault() is { Length: > 0 } id ? id : null;

    /// <summary>
    /// What groups sessions by artist: the library id, or the lowercase name when there is none.
    /// </summary>
    /// <param name="play">The session.</param>
    /// <returns>The grouping key.</returns>
    public static string ArtistKey(ListeningPlay play) => ArtistId(play) ?? ArtistName(play).ToLowerInvariant();

    private static Dictionary<string, object?> Totals(bool video, IReadOnlyList<ListeningPlay> rows)
    {
        var totals = new Dictionary<string, object?> { ["TotalSeconds"] = Math.Round(rows.Sum(r => r.SecondsListened)) };
        var counted = rows.Where(r => r.Counted).ToList();
        if (video)
        {
            totals["Views"] = counted.Count;
            totals["Movies"] = counted.Count(r => r.Kind == "movie");
            totals["Episodes"] = counted.Count(r => r.Kind == "episode");
            totals["DistinctMovies"] = counted.Where(r => r.Kind == "movie").Select(r => r.MovieKey).Distinct().Count();
            totals["DistinctShows"] = counted.Where(r => r.Kind == "episode").Select(r => r.SeriesKey).Distinct().Count();
        }
        else
        {
            totals["Plays"] = counted.Count;
            totals["DistinctSongs"] = counted.Select(r => r.SongKey).Distinct().Count();
            totals["DistinctArtists"] = counted.Select(ArtistKey).Distinct().Count();
            totals["DistinctAlbums"] = counted.Select(r => r.AlbumKey).Distinct().Count();
            totals["DistinctGenres"] = counted.SelectMany(r => Names(r.GenresJson)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        }

        return totals;
    }

    private static void AddTops(bool video, Dictionary<string, object?> target, IReadOnlyList<ListeningPlay> rows, IReadOnlyList<ListeningPlay> earlier, Func<IEnumerable<Guid>, Guid?> resolve, Func<ListeningPlay, IReadOnlyList<string>>? actors)
    {
        var counted = rows.Where(r => r.Counted).ToList();
        target["TopGenres"] = rows
            .SelectMany(r => Names(r.GenresJson).Select(g => (g, r.SecondsListened)))
            .GroupBy(x => x.g, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Sum(x => x.SecondsListened))
            .Take(5)
            .Select(g => new Dictionary<string, object?> { ["Name"] = g.Key, ["Seconds"] = Math.Round(g.Sum(x => x.SecondsListened)) })
            .ToList();

        if (video)
        {
            target["TopShows"] = rows.Where(r => r.Kind == "episode")
                .GroupBy(r => r.SeriesKey ?? string.Empty)
                .OrderByDescending(g => g.Sum(r => r.SecondsListened))
                .Take(10)
                .Select(g => new Dictionary<string, object?>
                {
                    ["SeriesId"] = g.Select(r => r.SeriesId).FirstOrDefault(i => i is not null)?.ToString("N"),
                    ["Name"] = g.Select(r => r.SeriesName).FirstOrDefault(n => !string.IsNullOrEmpty(n)),
                    ["Seconds"] = Math.Round(g.Sum(r => r.SecondsListened)),
                    ["Episodes"] = g.Where(r => r.Counted).Select(r => r.ItemId).Distinct().Count()
                }).ToList();
            target["TopMovies"] = rows.Where(r => r.Kind == "movie")
                .GroupBy(r => r.MovieKey ?? string.Empty)
                .OrderByDescending(g => g.Sum(r => r.SecondsListened))
                .Take(10)
                .Select(g => new Dictionary<string, object?>
                {
                    ["ItemId"] = resolve(g.Select(r => r.ItemId))?.ToString("N") ?? string.Empty,
                    ["Title"] = g.First().Title,
                    ["Views"] = g.Count(r => r.Counted),
                    ["Seconds"] = Math.Round(g.Sum(r => r.SecondsListened))
                }).ToList();
            var seenShows = earlier.Where(r => r.Kind == "episode").Select(r => r.SeriesKey).ToHashSet();
            target["NewShows"] = counted.Where(r => r.Kind == "episode" && !seenShows.Contains(r.SeriesKey))
                .GroupBy(r => r.SeriesKey)
                .Select(g => new Dictionary<string, object?> { ["SeriesId"] = g.First().SeriesId?.ToString("N"), ["Name"] = g.First().SeriesName })
                .ToList();
            target["TopActors"] = TopActors(rows, actors);
            return;
        }

        target["TopArtists"] = rows
            .GroupBy(ArtistKey)
            .OrderByDescending(g => g.Sum(r => r.SecondsListened))
            .Take(10)
            .Select(g => new Dictionary<string, object?>
            {
                ["ArtistId"] = ArtistId(g.First()),
                ["Name"] = ArtistName(g.First()),
                ["Seconds"] = Math.Round(g.Sum(r => r.SecondsListened)),
                ["Plays"] = g.Count(r => r.Counted)
            }).ToList();
        target["TopSongs"] = counted
            .GroupBy(r => r.SongKey ?? string.Empty)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Sum(r => r.SecondsListened))
            .Take(10)
            .Select(g => new Dictionary<string, object?>
            {
                ["SongKey"] = g.Key,
                ["ItemId"] = resolve(g.OrderByDescending(r => r.StartedAt).Select(r => r.ItemId).Distinct())?.ToString("N") ?? string.Empty,
                ["Title"] = g.First().Title,
                ["Artist"] = ArtistName(g.First()),
                ["Plays"] = g.Count(),
                ["Seconds"] = Math.Round(g.Sum(r => r.SecondsListened))
            }).ToList();
        target["TopAlbums"] = rows
            .Where(r => !string.IsNullOrEmpty(r.AlbumKey) && r.AlbumKey != "|")
            .GroupBy(r => r.AlbumKey!)
            .OrderByDescending(g => g.Sum(r => r.SecondsListened))
            .Take(5)
            .Select(g => new Dictionary<string, object?>
            {
                ["AlbumId"] = g.Select(r => r.AlbumId).FirstOrDefault(i => i is not null)?.ToString("N"),
                ["Name"] = g.First().Album,
                ["Artist"] = g.First().AlbumArtist ?? ArtistName(g.First()),
                ["Seconds"] = Math.Round(g.Sum(r => r.SecondsListened)),
                ["Plays"] = g.Count(r => r.Counted)
            }).ToList();
        var seenArtists = earlier.Select(ArtistKey).ToHashSet();
        var newArtists = counted.Where(r => !seenArtists.Contains(ArtistKey(r))).GroupBy(ArtistKey).ToList();
        target["NewArtists"] = newArtists
            .Select(g => new Dictionary<string, object?> { ["ArtistId"] = ArtistId(g.First()), ["Name"] = ArtistName(g.First()) })
            .ToList();
        target["NewArtistCount"] = newArtists.Count;
        var best = target["TopArtists"] is List<Dictionary<string, object?>> { Count: > 0 } top ? top[0] : null;
        target["TopArtist"] = best is null
            ? null
            : new Dictionary<string, object?>
            {
                ["ArtistId"] = best["ArtistId"],
                ["Name"] = best["Name"],
                ["Seconds"] = best["Seconds"],
                ["Plays"] = best["Plays"],
                ["FirstPlayedAt"] = rows.Concat(earlier).Where(r => ArtistKey(r) == ArtistKeyFor(best)).Min(r => (DateTime?)r.StartedAt)
            };
    }

    private static string ArtistKeyFor(Dictionary<string, object?> artist)
        => !string.IsNullOrEmpty(artist["ArtistId"] as string) ? (string)artist["ArtistId"]! : ((string)artist["Name"]!).ToLowerInvariant();

    private static List<Dictionary<string, object?>> Days(IReadOnlyList<ListeningPlay> rows, DateOnly from, DateOnly to, TimeZoneInfo zone)
    {
        var byDay = rows.GroupBy(r => DateOnly.FromDateTime(Local(r, zone))).ToDictionary(g => g.Key);
        var list = new List<Dictionary<string, object?>>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            byDay.TryGetValue(day, out var group);
            list.Add(new Dictionary<string, object?>
            {
                ["Date"] = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["Seconds"] = Math.Round(group?.Sum(r => r.SecondsListened) ?? 0),
                ["Plays"] = group?.Count(r => r.Counted) ?? 0
            });
        }

        return list;
    }

    private static Dictionary<string, object?>? BusiestDay(List<Dictionary<string, object?>> days)
    {
        var best = days.OrderByDescending(d => (double)d["Seconds"]!).FirstOrDefault();
        return best is null || (double)best["Seconds"]! <= 0
            ? null
            : new Dictionary<string, object?> { ["Date"] = best["Date"], ["Seconds"] = best["Seconds"] };
    }

    private static string? TopNameOnDay(bool video, IReadOnlyList<ListeningPlay> rows, TimeZoneInfo zone, string date)
    {
        var day = rows.Where(r => Local(r, zone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) == date).ToList();
        return video
            ? day.GroupBy(r => r.SeriesName ?? r.Title).OrderByDescending(g => g.Sum(r => r.SecondsListened)).FirstOrDefault()?.Key
            : day.GroupBy(ArtistKey).OrderByDescending(g => g.Sum(r => r.SecondsListened)).Select(g => ArtistName(g.First())).FirstOrDefault();
    }

    private static int LongestStreak(IReadOnlyList<ListeningPlay> rows, TimeZoneInfo zone)
    {
        var days = rows.Where(r => r.Counted).Select(r => DateOnly.FromDateTime(Local(r, zone))).Distinct().Order().ToList();
        int best = 0, run = 0;
        for (var i = 0; i < days.Count; i++)
        {
            run = i > 0 && days[i].DayNumber == days[i - 1].DayNumber + 1 ? run + 1 : 1;
            best = Math.Max(best, run);
        }

        return best;
    }

    private static List<Dictionary<string, object?>> ByMonth(bool video, IReadOnlyList<ListeningPlay> rows, DateOnly from, DateOnly to, TimeZoneInfo zone)
    {
        var list = new List<Dictionary<string, object?>>();
        // A year in review lists all twelve months, even though it stops a few days before the year ends
        var last = from is { Month: 1, Day: 1 } && to.Year == from.Year && to.DayNumber - from.DayNumber > 300 ? new DateOnly(from.Year, 12, 1) : to;
        for (var month = new DateOnly(from.Year, from.Month, 1); month <= last; month = month.AddMonths(1))
        {
            var inMonth = rows.Where(r => Local(r, zone).Year == month.Year && Local(r, zone).Month == month.Month).ToList();
            var top = video
                ? inMonth.GroupBy(r => r.SeriesName ?? r.Title).OrderByDescending(g => g.Sum(r => r.SecondsListened)).FirstOrDefault()?.Key
                : inMonth.GroupBy(ArtistKey).OrderByDescending(g => g.Sum(r => r.SecondsListened)).Select(g => ArtistName(g.First())).FirstOrDefault();
            list.Add(new Dictionary<string, object?>
            {
                ["Month"] = month.Month,
                ["Seconds"] = Math.Round(inMonth.Sum(r => r.SecondsListened)),
                [video ? "TopShow" : "TopArtist"] = top
            });
        }

        return list;
    }

    private static List<Dictionary<string, object?>> TopActors(IReadOnlyList<ListeningPlay> rows, Func<ListeningPlay, IReadOnlyList<string>>? actors)
    {
        if (actors is null)
        {
            return [];
        }

        var seconds = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var play in rows)
        {
            foreach (var name in actors(play))
            {
                seconds[name] = seconds.GetValueOrDefault(name) + play.SecondsListened;
            }
        }

        return seconds.OrderByDescending(p => p.Value).Take(5)
            .Select(p => new Dictionary<string, object?> { ["Name"] = p.Key, ["Seconds"] = Math.Round(p.Value) })
            .ToList();
    }

    private static int ShowsCompleted(IReadOnlyList<ListeningPlay> rows, IReadOnlyList<ListeningPlay> earlier, Func<Guid, int>? episodeCount)
    {
        if (episodeCount is null)
        {
            return 0;
        }

        // A show is finished when every episode the library has was watched at some point, and some of it was watched in this period
        var watched = rows.Concat(earlier).Where(r => r.Counted && r.Kind == "episode" && r.SeriesId is not null)
            .GroupBy(r => r.SeriesId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(r => r.ItemId).Distinct().Count());
        return rows.Where(r => r.Kind == "episode" && r.SeriesId is not null).Select(r => r.SeriesId!.Value).Distinct()
            .Count(id => episodeCount(id) is var total && total > 0 && watched.GetValueOrDefault(id) >= total);
    }

    private static string TimeOfDay(List<Dictionary<string, object?>> hours)
    {
        double Sum(int a, int b) => hours.Where(h => (int)h["Hour"]! >= a && (int)h["Hour"]! < b).Sum(h => (double)h["Seconds"]!);
        var parts = new[] { ("morning", Sum(5, 12)), ("afternoon", Sum(12, 17)), ("evening", Sum(17, 22)), ("night", Sum(22, 24) + Sum(0, 5)) };
        return parts.OrderByDescending(p => p.Item2).First().Item1;
    }

    private static Dictionary<string, object?> Pick(ListeningPlay play, TimeZoneInfo zone) => new()
    {
        ["Title"] = play.Title,
        ["Artist"] = play.Kind is "movie" or "episode" ? play.SeriesName : ArtistName(play),
        ["At"] = Local(play, zone)
    };

    private static int TopSongPlays(IReadOnlyList<ListeningPlay> rows)
        => rows.Where(r => r.Counted).GroupBy(r => r.SongKey).Select(g => g.Count()).DefaultIfEmpty(0).Max();

    private static Dictionary<string, object?>? MostRewatched(IReadOnlyList<ListeningPlay> rows)
    {
        var best = rows.Where(r => r.Counted).GroupBy(r => r.ItemId).OrderByDescending(g => g.Count()).FirstOrDefault();
        return best is null || best.Count() < 2
            ? null
            : new Dictionary<string, object?> { ["Title"] = best.First().Title, ["Views"] = best.Count() };
    }

    private static Dictionary<string, object?>? LongestBinge(IReadOnlyList<ListeningPlay> rows, TimeZoneInfo zone)
    {
        var best = rows.Where(r => r.Counted && r.Kind == "episode")
            .GroupBy(r => (Day: DateOnly.FromDateTime(Local(r, zone)), Show: r.SeriesKey))
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();
        return best is null
            ? null
            : new Dictionary<string, object?>
            {
                ["Show"] = best.First().SeriesName,
                ["Episodes"] = best.Count(),
                ["Seconds"] = Math.Round(best.Sum(r => r.SecondsListened)),
                ["Date"] = best.Key.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            };
    }

    private static List<(string Key, string Name)> Ranking(bool video, IReadOnlyList<ListeningPlay> rows)
        => video
            ? rows.GroupBy(r => r.Kind == "movie" ? r.MovieKey ?? string.Empty : r.SeriesKey ?? string.Empty)
                .OrderByDescending(g => g.Sum(r => r.SecondsListened))
                .Select(g => (g.Key, g.First().SeriesName ?? g.First().Title ?? string.Empty)).ToList()
            : rows.GroupBy(ArtistKey)
                .OrderByDescending(g => g.Sum(r => r.SecondsListened))
                .Select(g => (g.Key, ArtistName(g.First()))).ToList();

    private static DateTime Local(ListeningPlay play, TimeZoneInfo zone) => TimeZoneInfo.ConvertTimeFromUtc(play.StartedAt, zone);

    private static int WeekdayNumber(DateTime local) => local.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)local.DayOfWeek;

    private static string[] Names(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
