using System;
using System.Collections.Generic;

namespace Khylo.Model.LiveChannels;

/// <summary>
/// A Khylo live channel with the programs in the requested time window.
/// </summary>
public class LiveChannelDto
{
    /// <summary>
    /// Gets or sets the channel id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the built-in channel key (e.g. "horror-movies", "network:nbc"), or null for custom channels.
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
    /// Gets or sets the guide category, e.g. TV, Movies, Networks or Collections.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the channel description.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the programs, in order: the one on now first, then what follows.
    /// </summary>
    public IReadOnlyList<LiveChannelProgramDto> Programs { get; set; } = Array.Empty<LiveChannelProgramDto>();
}
