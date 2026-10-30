namespace Khylo.Providers.Podcasts.Online;

/// <summary>
/// A podcast in Apple's podcast directory.
/// </summary>
public class PodcastListing
{
    /// <summary>Gets or sets Apple's podcast id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the podcast's name.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets who makes it.</summary>
    public string? Author { get; set; }

    /// <summary>Gets or sets the RSS feed address.</summary>
    public string? FeedUrl { get; set; }

    /// <summary>Gets or sets the artwork address.</summary>
    public string? ArtworkUrl { get; set; }

    /// <summary>Gets or sets the main category.</summary>
    public string? Genre { get; set; }
}
