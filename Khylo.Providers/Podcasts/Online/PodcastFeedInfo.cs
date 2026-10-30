using System.Collections.Generic;

namespace Khylo.Providers.Podcasts.Online;

/// <summary>
/// What a podcast's RSS feed says about the show.
/// </summary>
public class PodcastFeedInfo
{
    /// <summary>Gets or sets the show's title.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the description, as plain text.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets who makes it.</summary>
    public string? Author { get; set; }

    /// <summary>Gets or sets the website.</summary>
    public string? Website { get; set; }

    /// <summary>Gets or sets the artwork address.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>Gets or sets the categories.</summary>
    public IReadOnlyList<string> Categories { get; set; } = [];
}
