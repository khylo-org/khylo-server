using System;
using System.IO;
using System.Linq;
using Khylo.Common.Configuration;
using Khylo.Controller.Library;
using Khylo.Server.Core.Listening;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Khylo.Server.Implementations.Tests.Listening;

public sealed class ListeningStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "khylo-listening-test-" + Guid.NewGuid().ToString("N"));
    private readonly ListeningStore _store;
    private readonly ListeningReports _reports;
    private readonly InsightsStore _insightsStore;
    private readonly Guid _person = Guid.NewGuid();

    public ListeningStoreTests()
    {
        // The made-up test history is only there when the test tools are switched on
        Environment.SetEnvironmentVariable("KHYLO_TEST_TOOLS", "1");
        Directory.CreateDirectory(_folder);
        var paths = new Mock<IApplicationPaths>();
        paths.SetupGet(p => p.DataPath).Returns(_folder);
        var library = new Mock<ILibraryManager>();
        _store = new ListeningStore(paths.Object, library.Object, NullLogger<ListeningStore>.Instance);
        _reports = new ListeningReports(_store, library.Object);
        _insightsStore = new InsightsStore(paths.Object);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("KHYLO_TEST_TOOLS", null);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_folder, true);
        }
        catch (IOException)
        {
            // A leftover temp folder is harmless
        }
    }

    [Fact]
    public void TestData_IsRefused_WhenTheTestToolsAreOff()
    {
        Environment.SetEnvironmentVariable("KHYLO_TEST_TOOLS", null);

        Assert.Throws<InvalidOperationException>(() => _reports.SeedTestData(_person, 30, false));

        Environment.SetEnvironmentVariable("KHYLO_TEST_TOOLS", "0");
        Assert.Throws<InvalidOperationException>(() => _reports.SeedTestData(_person, 30, false));
    }

    [Fact]
    public void Insert_ThenLoad_ReturnsTheSameValues()
    {
        var at = new DateTime(2026, 11, 5, 18, 30, 0, DateTimeKind.Utc);
        var play = new ListeningPlay
        {
            UserId = _person,
            ItemId = Guid.NewGuid(),
            StartedAt = at,
            SecondsListened = 123.5,
            DurationSeconds = 200,
            Counted = true,
            Kind = "music",
            Client = "iOS",
            DeviceName = "Phone",
            Title = "Song",
            Album = "Album",
            AlbumId = Guid.NewGuid(),
            AlbumArtist = "Artist",
            ArtistsJson = "[\"Artist\"]",
            GenresJson = "[\"Rock\"]",
            Year = 1999,
            SongKey = "song|artist",
            AlbumKey = "album|artist",
            IsLocal = true
        };

        var id = _store.Insert(play, out var isNew);
        var loaded = _store.Load(_person, at.AddHours(-1), at.AddHours(1), ["music"]).Single();

        Assert.True(isNew);
        Assert.Equal(id, loaded.Id);
        Assert.Equal(play.ItemId, loaded.ItemId);
        Assert.Equal(at, loaded.StartedAt);
        Assert.Equal(123.5, loaded.SecondsListened);
        Assert.True(loaded.Counted);
        Assert.Equal("iOS", loaded.Client);
        Assert.Equal(play.AlbumId, loaded.AlbumId);
        Assert.Equal(1999, loaded.Year);
        Assert.Equal("song|artist", loaded.SongKey);
        Assert.True(loaded.IsLocal);
        Assert.Null(loaded.SeriesId);
    }

    [Fact]
    public void Insert_TwiceWithinSeconds_IsOneSession()
    {
        var play = new ListeningPlay { UserId = _person, ItemId = Guid.NewGuid(), StartedAt = DateTime.UtcNow, Kind = "music" };
        var first = _store.Insert(play, out _);
        var second = _store.Insert(new ListeningPlay { UserId = _person, ItemId = play.ItemId, StartedAt = play.StartedAt.AddSeconds(2), Kind = "music" }, out var isNew);

        Assert.Equal(first, second);
        Assert.False(isNew);
    }

    [Fact]
    public void Update_ChangesTimeAndCounted()
    {
        var play = new ListeningPlay { UserId = _person, ItemId = Guid.NewGuid(), StartedAt = DateTime.UtcNow, Kind = "music" };
        var id = _store.Insert(play, out _);

        _store.Update(id, 61, true, 1000);

        var loaded = _store.LoadBefore(_person, DateTime.UtcNow.AddMinutes(1), 10, ["music"]).Single();
        Assert.Equal(61, loaded.SecondsListened);
        Assert.True(loaded.Counted);
        Assert.Equal(1000, loaded.BytesSent);
    }

    [Fact]
    public void Update_KeepsTheEarlierPlayMethodWhenNoneIsGiven_AndStoresTranscodeReasons()
    {
        var play = new ListeningPlay { UserId = _person, ItemId = Guid.NewGuid(), StartedAt = DateTime.UtcNow, Kind = "movie", PlayMethod = "DirectPlay" };
        var id = _store.Insert(play, out _);

        _store.Update(id, 10, false, null);
        _store.Update(id, 20, false, null, "Transcode", "VideoCodecNotSupported, AudioCodecNotSupported");
        _store.Update(id, 30, true, null);

        var loaded = _store.LoadBefore(_person, DateTime.UtcNow.AddMinutes(1), 10, ["movie"]).Single();
        Assert.Equal("Transcode", loaded.PlayMethod);
        Assert.Equal("VideoCodecNotSupported, AudioCodecNotSupported", loaded.TranscodeReasons);
    }

    [Fact]
    public void Recording_IsOnByDefault_CanBeTurnedOff_AndDeleteRemovesHistory()
    {
        var other = Guid.NewGuid();
        _store.Insert(new ListeningPlay { UserId = _person, ItemId = Guid.NewGuid(), StartedAt = DateTime.UtcNow, Kind = "music" }, out _);
        _store.Insert(new ListeningPlay { UserId = other, ItemId = Guid.NewGuid(), StartedAt = DateTime.UtcNow, Kind = "music" }, out _);

        Assert.True(_store.IsRecordingEnabled(_person));
        _store.SetRecordingEnabled(_person, false);
        Assert.False(_store.IsRecordingEnabled(_person));

        _store.DeleteForUser(_person);

        Assert.Empty(_store.Load(_person, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), ["music"]));
        Assert.Single(_store.Load(other, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), ["music"]));
        Assert.True(_store.IsRecordingEnabled(_person));
    }

    [Fact]
    public void Load_ForEveryone_LeavesOutPeopleWhoTurnedRecordingOff()
    {
        var quiet = Guid.NewGuid();
        _store.Insert(new ListeningPlay { UserId = _person, ItemId = Guid.NewGuid(), StartedAt = DateTime.UtcNow, Kind = "music" }, out _);
        _store.Insert(new ListeningPlay { UserId = quiet, ItemId = Guid.NewGuid(), StartedAt = DateTime.UtcNow, Kind = "music" }, out _);
        _store.SetRecordingEnabled(quiet, false);

        var everyone = _store.Load(null, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), ["music"]);

        Assert.Equal(_person, Assert.Single(everyone).UserId);
    }

    [Fact]
    public void Snapshots_KeepTheFirstOneStored()
    {
        _store.PutSnapshot(_person, "wrapped:music:2026", "{\"a\":1}");
        _store.PutSnapshot(_person, "wrapped:music:2026", "{\"a\":2}");

        Assert.Equal("{\"a\":1}", _store.GetSnapshot(_person, "wrapped:music:2026"));
        Assert.Null(_store.GetSnapshot(Guid.NewGuid(), "wrapped:music:2026"));
    }

    [Fact]
    public void SeededTestData_BuildsReviews_AndIsRemovedCleanly()
    {
        var added = _reports.SeedTestData(_person, 30, false);
        Assert.True(added > 10);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var summary = Assert.IsType<System.Collections.Generic.Dictionary<string, object?>>(_reports.Summary(_person, today.AddDays(-6), today, "UTC", false));
        Assert.True((int)summary["Plays"]! >= 0);

        var wrapped = _reports.Wrapped(_person, DateTime.UtcNow.Year, "UTC", false, true);
        Assert.NotNull(wrapped);

        _reports.DeleteTestData(_person);
        Assert.Empty(_store.Load(null, DateTime.UtcNow.AddYears(-1), DateTime.UtcNow.AddDays(1), ["music"]));
    }

    [Fact]
    public void Insights_Overview_SeesSeededVideoFromTheHousehold()
    {
        _reports.SeedTestData(_person, 20, true);
        var users = new Mock<IUserManager>();
        var library = new Mock<ILibraryManager>();
        var insights = new ListeningInsights(_store, _insightsStore, users.Object, library.Object);

        var overview = Assert.IsType<System.Collections.Generic.Dictionary<string, object?>>(insights.Overview(30, "UTC", "all"));

        Assert.True((int)overview["ActiveUsers"]! >= 2);
        Assert.True((int)overview["Views"]! > 0);
    }

    [Fact]
    public void InsightsStore_ReturnsPointsInSteps()
    {
        var now = DateTime.UtcNow;
        _insightsStore.Add(now.AddMinutes(-2), new InsightsSample(10, 100, 1, 0, 500, 0));
        _insightsStore.Add(now.AddMinutes(-1), new InsightsSample(30, 300, 2, 1, 700, 100));

        var points = _insightsStore.Points(now.AddHours(-1), now, 60);

        Assert.NotEmpty(points);
        Assert.Equal(2, points.Max(p => (int)p["Streams"]!));
    }
}
