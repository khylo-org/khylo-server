using System;
using Khylo.Server.Core.Listening;
using Xunit;

namespace Khylo.Server.Implementations.Tests.Listening;

public class ListeningKeysTests
{
    [Theory]
    [InlineData("Heroes", "Heroes (2017 Remaster)")]
    [InlineData("Heroes", "Heroes - Remastered 2011")]
    [InlineData("Heroes", "HEROES [Live]")]
    [InlineData("Heroes", "Heroes (feat. Someone)")]
    [InlineData("Heroes", "Heroes - Mono Version")]
    public void SongKey_IgnoresEditionNoise(string plain, string noisy)
        => Assert.Equal(ListeningKeys.SongKey(plain, "David Bowie"), ListeningKeys.SongKey(noisy, "David Bowie"));

    [Fact]
    public void SongKey_KeepsDifferentSongsApart()
        => Assert.NotEqual(ListeningKeys.SongKey("Heroes", "David Bowie"), ListeningKeys.SongKey("Changes", "David Bowie"));

    [Fact]
    public void SongKey_IgnoresCaseAndPunctuation()
        => Assert.Equal(ListeningKeys.SongKey("Don't Stop", "Fleetwood Mac"), ListeningKeys.SongKey("DONT STOP!", "fleetwood mac"));

    [Theory]
    [InlineData("Rumours (Deluxe Edition)")]
    [InlineData("Rumours (Expanded)")]
    [InlineData("Rumours (2013 Remaster)")]
    [InlineData("Rumours Deluxe")]
    [InlineData("Rumours - Remastered")]
    public void AlbumKey_GroupsEditionsOfOneAlbum(string edition)
        => Assert.Equal(ListeningKeys.AlbumKey("Rumours", "Fleetwood Mac"), ListeningKeys.AlbumKey(edition, "Fleetwood Mac"));

    [Fact]
    public void SeriesKey_IgnoresATrailingYear()
        => Assert.Equal(ListeningKeys.SeriesKey("Doctor Who"), ListeningKeys.SeriesKey("Doctor Who (2005)"));

    [Fact]
    public void MovieKey_UsesTitleAndYear()
    {
        Assert.Equal("heat|1995", ListeningKeys.MovieKey("Heat", 1995));
        Assert.NotEqual(ListeningKeys.MovieKey("Heat", 1995), ListeningKeys.MovieKey("Heat", 1986));
    }

    [Theory]
    [InlineData(false, 29, 200, false)]
    [InlineData(false, 30, 200, true)]
    [InlineData(false, 12, 20, true)]
    [InlineData(false, 9, 20, false)]
    [InlineData(false, 25, 59, false)]
    [InlineData(true, 299, 7200, false)]
    [InlineData(true, 300, 7200, true)]
    [InlineData(true, 250, 1200, true)]
    [InlineData(true, 200, 1200, false)]
    public void IsCounted_FollowsTheRules(bool video, double seconds, double duration, bool expected)
        => Assert.Equal(expected, ListeningKeys.IsCounted(video, seconds, duration));

    [Fact]
    public void ListenedSince_AddsTimeBetweenReportsWhilePlaying()
        => Assert.Equal(10, ListeningKeys.ListenedSince(0, 10 * TimeSpan.TicksPerSecond, 10, false));

    [Fact]
    public void ListenedSince_AddsNothingWhilePaused()
        => Assert.Equal(0, ListeningKeys.ListenedSince(0, 10 * TimeSpan.TicksPerSecond, 10, true));

    [Fact]
    public void ListenedSince_IgnoresASeek()
    {
        Assert.Equal(0, ListeningKeys.ListenedSince(100 * TimeSpan.TicksPerSecond, 20 * TimeSpan.TicksPerSecond, 10, false));
        Assert.Equal(0, ListeningKeys.ListenedSince(0, 90 * TimeSpan.TicksPerSecond, 10, false));
    }
}
