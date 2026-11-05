using System;

namespace Khylo.Server.Core.Listening;

/// <summary>
/// One listening or watching session of one item: who, what, when, how long, and what the item was called at the time.
/// </summary>
public class ListeningPlay
{
    /// <summary>Gets or sets the row id (0 until stored).</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets the person.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets the item.</summary>
    public Guid ItemId { get; set; }

    /// <summary>Gets or sets when it started (UTC).</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>Gets or sets the seconds actually heard or watched.</summary>
    public double SecondsListened { get; set; }

    /// <summary>Gets or sets the item's length in seconds.</summary>
    public double DurationSeconds { get; set; }

    /// <summary>Gets or sets a value indicating whether this is a real play, not just time.</summary>
    public bool Counted { get; set; }

    /// <summary>Gets or sets the kind: music, audiobook, podcast, movie or episode.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Gets or sets the app that played it.</summary>
    public string? Client { get; set; }

    /// <summary>Gets or sets the device that played it.</summary>
    public string? DeviceName { get; set; }

    /// <summary>Gets or sets the title at play time.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the album at play time.</summary>
    public string? Album { get; set; }

    /// <summary>Gets or sets the album id.</summary>
    public Guid? AlbumId { get; set; }

    /// <summary>Gets or sets the album artist at play time.</summary>
    public string? AlbumArtist { get; set; }

    /// <summary>Gets or sets the artist names, as a JSON list.</summary>
    public string? ArtistsJson { get; set; }

    /// <summary>Gets or sets the artist ids, as a JSON list.</summary>
    public string? ArtistIdsJson { get; set; }

    /// <summary>Gets or sets the genres, as a JSON list.</summary>
    public string? GenresJson { get; set; }

    /// <summary>Gets or sets the year.</summary>
    public int? Year { get; set; }

    /// <summary>Gets or sets the key that groups the same song across editions.</summary>
    public string? SongKey { get; set; }

    /// <summary>Gets or sets the key that groups editions of one album.</summary>
    public string? AlbumKey { get; set; }

    /// <summary>Gets or sets the series id (episodes).</summary>
    public Guid? SeriesId { get; set; }

    /// <summary>Gets or sets the series name (episodes).</summary>
    public string? SeriesName { get; set; }

    /// <summary>Gets or sets the season number (episodes).</summary>
    public int? SeasonNumber { get; set; }

    /// <summary>Gets or sets the episode number (episodes).</summary>
    public int? EpisodeNumber { get; set; }

    /// <summary>Gets or sets the library the item is in.</summary>
    public Guid? LibraryId { get; set; }

    /// <summary>Gets or sets how it was played (DirectPlay, DirectStream, Transcode).</summary>
    public string? PlayMethod { get; set; }

    /// <summary>Gets or sets why it was transcoded.</summary>
    public string? TranscodeReasons { get; set; }

    /// <summary>Gets or sets the resolution, for example 1080p.</summary>
    public string? Resolution { get; set; }

    /// <summary>Gets or sets the bitrate in bits per second.</summary>
    public long? Bitrate { get; set; }

    /// <summary>Gets or sets the video codec.</summary>
    public string? VideoCodec { get; set; }

    /// <summary>Gets or sets a value indicating whether the person was on the home network.</summary>
    public bool? IsLocal { get; set; }

    /// <summary>Gets or sets the bytes sent (an estimate from bitrate and time).</summary>
    public long? BytesSent { get; set; }

    /// <summary>Gets or sets the key that groups a show across seasons.</summary>
    public string? SeriesKey { get; set; }

    /// <summary>Gets or sets the key that groups a movie across copies.</summary>
    public string? MovieKey { get; set; }
}
