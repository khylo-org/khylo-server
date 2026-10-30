using System;
using System.Collections.Generic;

namespace Khylo.Model.LiveChannels;

/// <summary>
/// Live Channels as shown to administrators: the master switch and every channel.
/// </summary>
public class LiveChannelAdminDto
{
    /// <summary>
    /// Gets or sets a value indicating whether Live Channels is turned on.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets every channel, by number.
    /// </summary>
    public IReadOnlyList<LiveChannelAdminChannelDto> Channels { get; set; } = Array.Empty<LiveChannelAdminChannelDto>();
}
