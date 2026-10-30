#pragma warning disable CA2227 // Ids are filled in by each import source

using System;
using System.Collections.Generic;

namespace Khylo.Api.Helpers.KhyloImport;

/// <summary>
/// One title's history for one person on the other server.
/// </summary>
public class ImportEntry
{
    /// <summary>Gets or sets what it is.</summary>
    public ImportEntryKind Kind { get; set; }

    /// <summary>Gets or sets the title (movie, episode, song, album or artist name).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the year.</summary>
    public int? Year { get; set; }

    /// <summary>Gets or sets outside ids, keyed "imdb", "tmdb", "tvdb", "mbtrack", "mbalbum", "mbartist".</summary>
    public Dictionary<string, string> Ids { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets the show's name, for episodes.</summary>
    public string? SeriesTitle { get; set; }

    /// <summary>Gets or sets the show's year, for episodes.</summary>
    public int? SeriesYear { get; set; }

    /// <summary>Gets or sets the show's outside ids, for episodes.</summary>
    public Dictionary<string, string> SeriesIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets the season number.</summary>
    public int? Season { get; set; }

    /// <summary>Gets or sets the episode number.</summary>
    public int? Episode { get; set; }

    /// <summary>Gets or sets the artist (songs and albums).</summary>
    public string? Artist { get; set; }

    /// <summary>Gets or sets the album (songs).</summary>
    public string? Album { get; set; }

    /// <summary>Gets or sets the length in ticks.</summary>
    public long? RunTimeTicks { get; set; }

    /// <summary>Gets or sets the file path on the other server.</summary>
    public string? Path { get; set; }

    /// <summary>Gets or sets a value indicating whether it was watched or listened to.</summary>
    public bool Played { get; set; }

    /// <summary>Gets or sets how many times.</summary>
    public int PlayCount { get; set; }

    /// <summary>Gets or sets where they stopped, in ticks.</summary>
    public long PositionTicks { get; set; }

    /// <summary>Gets or sets when it was last played.</summary>
    public DateTime? LastPlayed { get; set; }

    /// <summary>Gets or sets a value indicating whether it's a favorite.</summary>
    public bool Favorite { get; set; }

    /// <summary>Gets or sets the rating, 0 to 10.</summary>
    public double? Rating { get; set; }

    /// <summary>
    /// Gets a short name for lists of what couldn't be found.
    /// </summary>
    /// <returns>The display name.</returns>
    public string Describe() => Kind switch
    {
        ImportEntryKind.Episode => $"{SeriesTitle} S{Season:00}E{Episode:00} · {Title}",
        ImportEntryKind.Track => $"{Artist} – {Title} ({Album})",
        ImportEntryKind.Album => $"{Artist} – {Title} (album)",
        ImportEntryKind.Artist => $"{Title} (artist)",
        ImportEntryKind.Series => $"{Title} (show)",
        _ => Year is null ? Title : $"{Title} ({Year})"
    };
}
