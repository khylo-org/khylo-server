using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Khylo.Server.Core.Listening;
using Xunit;

namespace Khylo.Server.Implementations.Tests.Listening;

public class ListeningStatsTests
{
    private static readonly Guid Person = Guid.NewGuid();

    private static ListeningPlay Song(string title, string artist, DateTime at, double seconds = 200, bool counted = true) => new()
    {
        UserId = Person,
        ItemId = Guid.NewGuid(),
        StartedAt = at,
        SecondsListened = seconds,
        DurationSeconds = 200,
        Counted = counted,
        Kind = "music",
        Title = title,
        Album = "Album",
        AlbumArtist = artist,
        ArtistsJson = JsonSerializer.Serialize(new[] { artist }),
        ArtistIdsJson = JsonSerializer.Serialize(new[] { string.Empty }),
        GenresJson = JsonSerializer.Serialize(new[] { "Rock" }),
        SongKey = ListeningKeys.SongKey(title, artist),
        AlbumKey = ListeningKeys.AlbumKey("Album", artist)
    };

    [Fact]
    public void Summary_CountsPlaysTimeAndTopLists()
    {
        var monday = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
        var rows = new List<ListeningPlay>
        {
            Song("One", "Ann", monday),
            Song("One (Remastered)", "Ann", monday.AddHours(1)),
            Song("Two", "Bob", monday.AddDays(1)),
            Song("Skipped", "Bob", monday.AddDays(1), 5, false)
        };

        var summary = ListeningStats.Summary(false, rows, [], [], new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 8), TimeZoneInfo.Utc, _ => null);

        Assert.Equal(3, summary["Plays"]);
        Assert.Equal(2, summary["DistinctSongs"]);
        Assert.Equal(605d, summary["TotalSeconds"]);
        var days = Assert.IsType<List<Dictionary<string, object?>>>(summary["Days"]);
        Assert.Equal(7, days.Count);
        var songs = Assert.IsType<List<Dictionary<string, object?>>>(summary["TopSongs"]);
        Assert.Equal(2, songs[0]["Plays"]);
        var artists = Assert.IsType<List<Dictionary<string, object?>>>(summary["TopArtists"]);
        Assert.Equal("Ann", artists[0]["Name"]);
    }

    [Fact]
    public void Summary_MarksArtistsHeardForTheFirstTimeAsNew()
    {
        var at = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
        var rows = new List<ListeningPlay> { Song("A", "Ann", at), Song("B", "Bob", at) };
        var earlier = new List<ListeningPlay> { Song("Old", "Ann", at.AddDays(-30)) };

        var summary = ListeningStats.Summary(false, rows, earlier, [], new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 8), TimeZoneInfo.Utc, _ => null);

        var fresh = Assert.IsType<List<Dictionary<string, object?>>>(summary["NewArtists"]);
        Assert.Equal("Bob", Assert.Single(fresh)["Name"]);
    }

    [Fact]
    public void Wrapped_OpensTheMondayAfterBlackFriday()
    {
        Assert.Equal(new DateOnly(2026, 11, 30), ListeningStats.WrappedOpens(2026));
        Assert.Equal(new DateOnly(2027, 11, 29), ListeningStats.WrappedOpens(2027));
    }

    [Fact]
    public void QuarterReview_OpensTheFirstMondayAfterTheQuarterEnds()
    {
        Assert.Equal(new DateOnly(2027, 1, 4), ListeningStats.QuarterOpens(2026, 4));
        Assert.Equal(new DateOnly(2027, 4, 5), ListeningStats.QuarterOpens(2027, 1));
    }

    [Fact]
    public void Review_ListsAllTwelveMonthsAndTheLongestStreak()
    {
        var rows = Enumerable.Range(0, 3).Select(d => Song("S" + d, "Ann", new DateTime(2026, 3, 10 + d, 20, 0, 0, DateTimeKind.Utc))).ToList();

        var review = ListeningStats.Review(false, rows, [], new DateOnly(2026, 1, 1), new DateOnly(2026, 11, 29), TimeZoneInfo.Utc, _ => null);

        Assert.Equal(12, Assert.IsType<List<Dictionary<string, object?>>>(review["ByMonth"]).Count);
        Assert.Equal(3, review["LongestStreakDays"]);
        Assert.Equal("evening", review["TimeOfDay"]);
        Assert.Equal(24, Assert.IsType<List<Dictionary<string, object?>>>(review["ByHour"]).Count);
    }

    private static ListeningPlay Episode(Guid series, int number, DateTime at) => new()
    {
        UserId = Person,
        ItemId = Guid.NewGuid(),
        StartedAt = at,
        SecondsListened = 1500,
        DurationSeconds = 1500,
        Counted = true,
        Kind = "episode",
        Title = "Episode " + number,
        SeriesId = series,
        SeriesName = "Show",
        SeriesKey = "show"
    };

    [Fact]
    public void VideoSummary_ListsTopActorsByTimeWatched()
    {
        var show = Guid.NewGuid();
        var at = new DateTime(2026, 11, 3, 20, 0, 0, DateTimeKind.Utc);
        var rows = new List<ListeningPlay> { Episode(show, 1, at), Episode(show, 2, at.AddHours(1)) };

        var summary = ListeningStats.Summary(true, rows, [], [], new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 8), TimeZoneInfo.Utc, _ => null, _ => ["Ann Actor", "Bob Actor"]);

        var actors = Assert.IsType<List<Dictionary<string, object?>>>(summary["TopActors"]);
        Assert.Equal(2, actors.Count);
        Assert.Equal(3000d, actors[0]["Seconds"]);
    }

    [Fact]
    public void Review_CountsAShowAsCompletedOnlyWhenEveryEpisodeWasWatched()
    {
        var done = Guid.NewGuid();
        var partial = Guid.NewGuid();
        var at = new DateTime(2026, 5, 3, 20, 0, 0, DateTimeKind.Utc);
        var rows = new List<ListeningPlay> { Episode(done, 1, at), Episode(done, 2, at.AddHours(1)), Episode(partial, 1, at) };

        var review = ListeningStats.Review(true, rows, [], new DateOnly(2026, 1, 1), new DateOnly(2026, 11, 29), TimeZoneInfo.Utc, _ => null, null, id => id.Equals(done) ? 2 : 5);

        Assert.Equal(1, review["ShowsCompleted"]);
    }
}
