using System;

namespace Khylo.Server.Implementations.LiveChannels;

/// <summary>
/// Administrator settings for Live Channels, saved in the configuration folder.
/// </summary>
internal sealed class LiveChannelSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether Live Channels is turned on.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the keys of the channels turned off. Keys are stable across restarts and lineup
    /// updates, so network and collection channels stay off too.
    /// </summary>
    public string[] DisabledChannels { get; set; } = Array.Empty<string>();
}
