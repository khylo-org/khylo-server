using System;

namespace Khylo.Model.LiveChannels;

/// <summary>
/// A channel as shown to administrators: whether it is on, and how much content it has.
/// </summary>
public class LiveChannelAdminChannelDto
{
    /// <summary>
    /// Gets or sets the channel id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the built-in channel key, or null for custom channels.
    /// </summary>
    public string? Key { get; set; }

    /// <summary>
    /// Gets or sets the channel number.
    /// </summary>
    public int Number { get; set; }

    /// <summary>
    /// Gets or sets the channel name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the lineup group: TV, Movies, Seasonal, Networks or Collections.
    /// </summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the channel description.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the channel is turned on.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets where the channel stands.
    /// </summary>
    public LiveChannelAdminState State { get; set; }

    /// <summary>
    /// Gets or sets how many titles the channel airs (or would air, if it is off).
    /// </summary>
    public int TitleCount { get; set; }

    /// <summary>
    /// Gets or sets the fewest titles the channel needs to go on the air.
    /// </summary>
    public int MinimumTitles { get; set; }

    /// <summary>
    /// Gets or sets the total runtime of the channel's content, in hours.
    /// </summary>
    public double Hours { get; set; }

    /// <summary>
    /// Gets or sets the least runtime the channel needs to go on the air, in hours.
    /// </summary>
    public double MinimumHours { get; set; }

    /// <summary>
    /// Gets or sets the months (1–12) a seasonal channel airs. Empty means all year.
    /// </summary>
    public int[] ActiveMonths { get; set; } = Array.Empty<int>();
}
