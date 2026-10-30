using System;
using System.Collections.Generic;

namespace Khylo.Server.Implementations.LiveChannels;

/// <summary>
/// A movie or series in the library snapshot that channels are built from.
/// </summary>
internal sealed class LiveChannelTitle
{
    /// <summary>
    /// Gets the movie or series id.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Gets the library the title belongs to.
    /// </summary>
    public required Guid LibraryId { get; init; }

    /// <summary>
    /// Gets a value indicating whether this is a series.
    /// </summary>
    public required bool IsSeries { get; init; }

    /// <summary>
    /// Gets the genres.
    /// </summary>
    public required string[] Genres { get; init; }

    /// <summary>
    /// Gets the tags.
    /// </summary>
    public required string[] Tags { get; init; }

    /// <summary>
    /// Gets the studios and networks.
    /// </summary>
    public required string[] Studios { get; init; }

    /// <summary>
    /// Gets the TMDB id, if known.
    /// </summary>
    public string? TmdbId { get; init; }

    /// <summary>
    /// Gets the community rating (0–10).
    /// </summary>
    public float? CommunityRating { get; init; }

    /// <summary>
    /// Gets when the title was added to the library.
    /// </summary>
    public DateTime DateCreated { get; init; }

    /// <summary>
    /// Gets the year a series first aired, or a movie's release year.
    /// </summary>
    public int? Year { get; init; }

    /// <summary>
    /// Gets the year a series ended, or null if it is still running or its status is unknown.
    /// </summary>
    public int? EndedYear { get; init; }

    /// <summary>
    /// Gets the parental rating score, if rated.
    /// </summary>
    public int? RatingScore { get; init; }

    /// <summary>
    /// Gets the movie itself, or the series' regular episodes in airing order.
    /// </summary>
    public required IReadOnlyList<LiveChannelUnitItem> Items { get; init; }
}
