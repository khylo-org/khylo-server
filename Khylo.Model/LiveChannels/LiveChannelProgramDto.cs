using System;
using Khylo.Data.Enums;

namespace Khylo.Model.LiveChannels;

/// <summary>
/// One scheduled program on a Khylo live channel.
/// </summary>
public class LiveChannelProgramDto
{
    /// <summary>
    /// Gets or sets the library item that airs.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets the item type.
    /// </summary>
    public BaseItemKind Type { get; set; }

    /// <summary>
    /// Gets or sets the item name (episode title for episodes).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the series name, for episodes.
    /// </summary>
    public string? SeriesName { get; set; }

    /// <summary>
    /// Gets or sets the series id, for episodes.
    /// </summary>
    public Guid? SeriesId { get; set; }

    /// <summary>
    /// Gets or sets the season number, for episodes.
    /// </summary>
    public int? SeasonNumber { get; set; }

    /// <summary>
    /// Gets or sets the episode number, for episodes.
    /// </summary>
    public int? EpisodeNumber { get; set; }

    /// <summary>
    /// Gets or sets the production year.
    /// </summary>
    public int? ProductionYear { get; set; }

    /// <summary>
    /// Gets or sets the official rating.
    /// </summary>
    public string? OfficialRating { get; set; }

    /// <summary>
    /// Gets or sets the overview.
    /// </summary>
    public string? Overview { get; set; }

    /// <summary>
    /// Gets or sets when the program starts (UTC).
    /// </summary>
    public DateTime StartUtc { get; set; }

    /// <summary>
    /// Gets or sets when the program ends (UTC).
    /// </summary>
    public DateTime EndUtc { get; set; }
}
