using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Khylo.Common;
using Khylo.Controller.Entities;
using Khylo.Controller.Library;
using Khylo.Controller.Listening;
using Khylo.Data.Enums;

namespace Khylo.Server.Core.Listening;

/// <summary>
/// Builds a person's own reviews from the listening history. It only ever reads the rows of the person it is asked about.
/// </summary>
public class ListeningReports : IListeningReports
{
    private readonly ListeningStore _store;
    private readonly ILibraryManager _library;
    private readonly Dictionary<Guid, IReadOnlyList<string>> _cast = new();
    private readonly Dictionary<Guid, int> _episodes = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ListeningReports"/> class.
    /// </summary>
    /// <param name="store">The history.</param>
    /// <param name="library">The library manager.</param>
    public ListeningReports(ListeningStore store, ILibraryManager library)
    {
        _store = store;
        _library = library;
    }

    private static bool IsDevBuild => KhyloBuildInfo.Number.StartsWith("DEV", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public object Summary(Guid userId, DateOnly from, DateOnly to, string? timeZone, bool video)
    {
        var zone = ListeningStats.Zone(timeZone);
        if (to < from)
        {
            (from, to) = (to, from);
        }

        var length = to.DayNumber - from.DayNumber + 1;
        var kinds = Kinds(video);
        var start = ListeningStats.StartOfDay(from, zone);
        var end = ListeningStats.StartOfDay(to.AddDays(1), zone);
        var rows = _store.Load(userId, start, end, kinds);
        var previous = _store.Load(userId, ListeningStats.StartOfDay(from.AddDays(-length), zone), start, kinds);
        var earlier = _store.LoadEarlier(userId, start, kinds);
        return ListeningStats.Summary(video, rows, earlier, previous, from, to, zone, Resolve, video ? Actors : null);
    }

    /// <inheritdoc />
    public object History(Guid userId, DateTime? before, int limit, bool video)
    {
        var rows = _store.LoadBefore(userId, before?.ToUniversalTime() ?? DateTime.UtcNow.AddMinutes(1), limit, Kinds(video));
        var items = rows.Select(r => new Dictionary<string, object?>
        {
            ["StartedAt"] = r.StartedAt,
            ["SecondsListened"] = Math.Round(r.SecondsListened),
            ["ItemId"] = Exists(r.ItemId) ? r.ItemId.ToString("N") : string.Empty,
            ["Kind"] = r.Kind,
            ["Title"] = r.Title,
            ["Album"] = r.Album,
            ["AlbumId"] = r.AlbumId?.ToString("N"),
            ["AlbumArtist"] = r.AlbumArtist,
            ["Artists"] = ParseNames(r.ArtistsJson),
            ["ArtistIds"] = ParseNames(r.ArtistIdsJson),
            ["Genres"] = ParseNames(r.GenresJson),
            ["Year"] = r.Year,
            ["SeriesName"] = r.SeriesName,
            ["SeasonNumber"] = r.SeasonNumber,
            ["EpisodeNumber"] = r.EpisodeNumber
        }).ToList();
        return new Dictionary<string, object?> { ["Items"] = items, ["Next"] = rows.Count == 0 ? null : rows[^1].StartedAt };
    }

    /// <inheritdoc />
    public object Wrapped(Guid userId, int year, string? timeZone, bool video, bool preview)
    {
        var zone = ListeningStats.Zone(timeZone);
        var opens = ListeningStats.WrappedOpens(year);
        var open = opens.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
        var previewing = preview && IsDevBuild;
        if (today < opens && !previewing)
        {
            return new Dictionary<string, object?> { ["Available"] = false, ["AvailableFrom"] = open };
        }

        var key = "wrapped:" + Kind(video) + ":" + year.ToString(CultureInfo.InvariantCulture);
        if (!previewing && _store.GetSnapshot(userId, key) is { } stored)
        {
            return JsonDocument.Parse(stored).RootElement.Clone();
        }

        var from = new DateOnly(year, 1, 1);
        var to = opens.AddDays(-1);
        var review = BuildReview(userId, video, from, to, zone);
        review["Available"] = true;
        review["Year"] = year;
        review["AvailableFrom"] = open;
        if (!previewing)
        {
            _store.PutSnapshot(userId, key, JsonSerializer.Serialize(review));
        }

        return review;
    }

    /// <inheritdoc />
    public object Quarter(Guid userId, int year, int quarter, string? timeZone, bool video, bool preview)
    {
        quarter = Math.Clamp(quarter, 1, 4);
        var zone = ListeningStats.Zone(timeZone);
        var opens = ListeningStats.QuarterOpens(year, quarter);
        var open = opens.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
        var previewing = preview && IsDevBuild;
        if (today < opens && !previewing)
        {
            return new Dictionary<string, object?> { ["Available"] = false, ["AvailableFrom"] = open };
        }

        var key = "quarter:" + Kind(video) + ":" + year.ToString(CultureInfo.InvariantCulture) + "q" + quarter.ToString(CultureInfo.InvariantCulture);
        if (!previewing && _store.GetSnapshot(userId, key) is { } stored)
        {
            return JsonDocument.Parse(stored).RootElement.Clone();
        }

        var from = ListeningStats.QuarterStart(year, quarter);
        var to = ListeningStats.QuarterEnd(year, quarter);
        var review = BuildReview(userId, video, from, to, zone);
        var previousYear = quarter == 1 ? year - 1 : year;
        var previousQuarter = quarter == 1 ? 4 : quarter - 1;
        var previousRows = _store.Load(
            userId,
            ListeningStats.StartOfDay(ListeningStats.QuarterStart(previousYear, previousQuarter), zone),
            ListeningStats.StartOfDay(from, zone),
            Kinds(video));
        var rows = _store.Load(userId, ListeningStats.StartOfDay(from, zone), ListeningStats.StartOfDay(to.AddDays(1), zone), Kinds(video));
        ListeningStats.AddComparison(video, review, rows, previousRows);
        review["Available"] = true;
        review["Year"] = year;
        review["Quarter"] = quarter;
        review["AvailableFrom"] = open;
        if (!previewing)
        {
            _store.PutSnapshot(userId, key, JsonSerializer.Serialize(review));
        }

        return review;
    }

    /// <inheritdoc />
    public object Quarters(Guid userId, string? timeZone, bool video)
    {
        var zone = ListeningStats.Zone(timeZone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
        var kinds = Kinds(video);
        var first = _store.FirstPlay(userId, kinds);
        var list = new List<Dictionary<string, object?>>();
        if (first is null)
        {
            return list;
        }

        var firstLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(first.Value, zone));
        var year = firstLocal.Year;
        var quarter = ((firstLocal.Month - 1) / 3) + 1;
        while (ListeningStats.QuarterStart(year, quarter) <= today)
        {
            var opens = ListeningStats.QuarterOpens(year, quarter);
            list.Add(new Dictionary<string, object?>
            {
                ["Year"] = year,
                ["Quarter"] = quarter,
                ["Available"] = today >= opens,
                ["AvailableFrom"] = opens.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            });
            quarter++;
            if (quarter > 4)
            {
                quarter = 1;
                year++;
            }
        }

        list.Reverse();
        return list;
    }

    /// <inheritdoc />
    public int SeedTestData(Guid userId, int days, bool video)
    {
        if (!IsDevBuild)
        {
            throw new InvalidOperationException("Test data is only available in Dev builds.");
        }

        days = Math.Clamp(days, 1, 400);
        var random = new Random(userId.GetHashCode() ^ (video ? 7 : 3));
        var count = 0;
        var people = video ? Enumerable.Range(0, 4).Select(i => i == 0 ? userId : Guid.Parse("00000000-0000-4000-8000-00000000000" + i)).ToList() : [userId];
        foreach (var person in people)
        {
            for (var d = 0; d < days; d++)
            {
                if (random.NextDouble() < 0.35)
                {
                    continue;
                }

                var sessions = random.Next(1, video ? 4 : 9);
                for (var s = 0; s < sessions; s++)
                {
                    var play = video ? FakeVideo(random) : FakeSong(random);
                    play.UserId = person;
                    play.StartedAt = DateTime.UtcNow.Date.AddDays(-d).AddHours(random.Next(7, 23)).AddMinutes(random.Next(0, 60));
                    play.Client = ListeningStore.TestClient;
                    play.DeviceName = random.Next(3) switch { 0 => "Living Room TV", 1 => "iPhone", _ => "Web" };
                    play.SecondsListened = Math.Round(play.DurationSeconds * (0.15 + (random.NextDouble() * 0.85)));
                    play.Counted = ListeningKeys.IsCounted(video, play.SecondsListened, play.DurationSeconds);
                    _store.Insert(play, out _);
                    count++;
                }
            }
        }

        return count;
    }

    /// <inheritdoc />
    public void DeleteTestData(Guid userId)
    {
        _store.DeleteTestData(userId);
        _store.DeleteAllTestData();
    }

    private static string Kind(bool video) => video ? "video" : "music";

    private static IReadOnlyCollection<string> Kinds(bool video) => video ? ListeningStats.VideoKinds : ListeningStats.MusicKinds;

    private static string[] ParseNames(string? json)
        => string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<string[]>(json) ?? [];

    private static ListeningPlay FakeSong(Random random)
    {
        var artist = random.Next(1, 25);
        var song = random.Next(1, 60);
        var genres = new[] { "Rock", "Pop", "Jazz", "Hip-Hop", "Electronic", "Country" };
        return new ListeningPlay
        {
            ItemId = Guid.NewGuid(),
            Kind = "music",
            Title = "Test Song " + song.ToString(CultureInfo.InvariantCulture),
            Album = "Test Album " + (((song - 1) / 6) + 1).ToString(CultureInfo.InvariantCulture),
            AlbumArtist = "Test Artist " + artist.ToString(CultureInfo.InvariantCulture),
            ArtistsJson = JsonSerializer.Serialize(new[] { "Test Artist " + artist.ToString(CultureInfo.InvariantCulture) }),
            ArtistIdsJson = JsonSerializer.Serialize(new[] { string.Empty }),
            GenresJson = JsonSerializer.Serialize(new[] { genres[artist % genres.Length] }),
            Year = 1990 + (song % 30),
            DurationSeconds = 150 + random.Next(0, 180),
            SongKey = ListeningKeys.SongKey("Test Song " + song, "Test Artist " + artist),
            AlbumKey = ListeningKeys.AlbumKey("Test Album " + (((song - 1) / 6) + 1), "Test Artist " + artist)
        };
    }

    private static ListeningPlay FakeVideo(Random random)
    {
        var genres = new[] { "Drama", "Comedy", "Sci-Fi", "Crime", "Documentary" };
        if (random.NextDouble() < 0.7)
        {
            var show = random.Next(1, 9);
            return new ListeningPlay
            {
                ItemId = Guid.NewGuid(),
                Kind = "episode",
                Title = "Episode " + random.Next(1, 11).ToString(CultureInfo.InvariantCulture),
                SeriesId = Guid.Parse("00000000-0000-4000-9000-00000000000" + show),
                SeriesName = "Test Show " + show.ToString(CultureInfo.InvariantCulture),
                SeasonNumber = random.Next(1, 4),
                EpisodeNumber = random.Next(1, 11),
                SeriesKey = ListeningKeys.SeriesKey("Test Show " + show),
                GenresJson = JsonSerializer.Serialize(new[] { genres[show % genres.Length] }),
                DurationSeconds = 1500 + random.Next(0, 1800),
                PlayMethod = random.Next(5) == 0 ? "Transcode" : "DirectPlay",
                Resolution = random.Next(3) == 0 ? "720p" : "1080p"
            };
        }

        var movie = random.Next(1, 15);
        return new ListeningPlay
        {
            ItemId = Guid.NewGuid(),
            Kind = "movie",
            Title = "Test Movie " + movie.ToString(CultureInfo.InvariantCulture),
            Year = 1980 + movie,
            MovieKey = ListeningKeys.MovieKey("Test Movie " + movie, 1980 + movie),
            GenresJson = JsonSerializer.Serialize(new[] { genres[movie % genres.Length] }),
            DurationSeconds = 5400 + random.Next(0, 3600),
            PlayMethod = random.Next(5) == 0 ? "Transcode" : "DirectPlay",
            Resolution = random.Next(3) == 0 ? "720p" : "1080p"
        };
    }

    private Dictionary<string, object?> BuildReview(Guid userId, bool video, DateOnly from, DateOnly to, TimeZoneInfo zone)
    {
        var start = ListeningStats.StartOfDay(from, zone);
        var rows = _store.Load(userId, start, ListeningStats.StartOfDay(to.AddDays(1), zone), Kinds(video));
        var earlier = _store.LoadEarlier(userId, start, Kinds(video));
        return ListeningStats.Review(video, rows, earlier, from, to, zone, Resolve, video ? Actors : null, video ? EpisodeCount : null);
    }

    private IReadOnlyList<string> Actors(ListeningPlay play)
    {
        // An episode's cast is the show's cast
        var id = play.Kind == "episode" && play.SeriesId is { } series ? series : play.ItemId;
        if (_cast.TryGetValue(id, out var known))
        {
            return known;
        }

        var names = _library.GetItemById(id) is { } item
            ? _library.GetPeople(item).Where(p => p.Type == PersonKind.Actor).Select(p => p.Name).Take(10).ToList()
            : [];
        _cast[id] = names;
        return names;
    }

    private int EpisodeCount(Guid seriesId)
    {
        if (!_episodes.TryGetValue(seriesId, out var count))
        {
            count = _library.GetCount(new InternalItemsQuery { AncestorIds = [seriesId], IncludeItemTypes = [BaseItemKind.Episode], Recursive = true });
            _episodes[seriesId] = count;
        }

        return count;
    }

    private bool Exists(Guid id) => _library.GetItemById(id) is not null;

    private Guid? Resolve(IEnumerable<Guid> ids)
    {
        foreach (var id in ids)
        {
            if (Exists(id))
            {
                return id;
            }
        }

        return null;
    }
}
