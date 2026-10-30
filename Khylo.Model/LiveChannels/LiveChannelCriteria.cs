using System;

namespace Khylo.Model.LiveChannels;

/// <summary>
/// The library content a Khylo live channel draws from, matched against each title's own metadata
/// (genres, first-air or release year, series status, official rating). Titles without genres are
/// never matched, since they can't be classified. Empty lists mean "no restriction".
/// </summary>
public class LiveChannelCriteria
{
    /// <summary>
    /// Gets or sets whether the channel airs TV series or movies.
    /// </summary>
    public LiveChannelContentType ContentType { get; set; } = LiveChannelContentType.Series;

    /// <summary>
    /// Gets or sets a value indicating whether the channel is a category channel. Each title airs on
    /// only one category channel: the first one (by channel number) it matches that is on the air.
    /// </summary>
    public bool Exclusive { get; set; }

    /// <summary>
    /// Gets or sets the genres; a title matches when it has any of them.
    /// </summary>
    public string[] Genres { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets genres a title must all have, e.g. Romance and Comedy for romantic comedies.
    /// </summary>
    public string[] RequiredGenres { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets genres that exclude a title.
    /// </summary>
    public string[] ExcludeGenres { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the tags; a title matches when it has any of them.
    /// </summary>
    public string[] Tags { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets tags (keywords) that exclude a title.
    /// </summary>
    public string[] ExcludeTags { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets themes matched against both genres and tags (keywords); a title matches when it
    /// has any of them. Covers providers that file the same theme as a genre or as a keyword.
    /// </summary>
    public string[] AnyOf { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the months (1–12) a seasonal channel is on the air. Empty means all year.
    /// </summary>
    public int[] ActiveMonths { get; set; } = Array.Empty<int>();

    /// <summary>
    /// Gets or sets the studios or networks; a title matches when it has any of them.
    /// </summary>
    public string[] Studios { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets specific titles (movies or series) to air, e.g. the movies of a collection.
    /// </summary>
    public Guid[] ItemIds { get; set; } = Array.Empty<Guid>();

    /// <summary>
    /// Gets or sets the lowest community rating (0–10).
    /// </summary>
    public double? MinCommunityRating { get; set; }

    /// <summary>
    /// Gets or sets how recently titles must have been added to the library, in days.
    /// </summary>
    public int? AddedWithinDays { get; set; }

    /// <summary>
    /// Gets or sets the earliest year (series: first aired; movies: released).
    /// </summary>
    public int? MinYear { get; set; }

    /// <summary>
    /// Gets or sets the latest year (series: first aired; movies: released).
    /// </summary>
    public int? MaxYear { get; set; }

    /// <summary>
    /// Gets or sets, for series, the latest year the series may have ended. Series still running
    /// (or without an end date) don't match.
    /// </summary>
    public int? EndedByYear { get; set; }

    /// <summary>
    /// Gets or sets the lowest official rating, e.g. "TV-PG". Unrated titles still match.
    /// </summary>
    public string? MinOfficialRating { get; set; }

    /// <summary>
    /// Gets or sets the highest official rating, e.g. "TV-Y7". Unrated titles don't match.
    /// </summary>
    public string? MaxOfficialRating { get; set; }

    /// <summary>
    /// Gets or sets the longest typical episode runtime in minutes, e.g. 35 for half-hour shows.
    /// </summary>
    public int? MaxEpisodeMinutes { get; set; }

    /// <summary>
    /// Gets or sets the libraries to draw from. Empty means every movie and TV library.
    /// </summary>
    public Guid[] LibraryIds { get; set; } = Array.Empty<Guid>();
}
