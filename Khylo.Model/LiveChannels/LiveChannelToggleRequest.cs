using System;

namespace Khylo.Model.LiveChannels;

/// <summary>
/// Turns one or more channels on or off.
/// </summary>
public class LiveChannelToggleRequest
{
    /// <summary>
    /// Gets or sets the channels.
    /// </summary>
    public Guid[] ChannelIds { get; set; } = Array.Empty<Guid>();

    /// <summary>
    /// Gets or sets a value indicating whether to turn them on.
    /// </summary>
    public bool Enabled { get; set; }
}
