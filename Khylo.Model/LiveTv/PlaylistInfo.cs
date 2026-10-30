using System;
using System.Collections.Generic;

namespace Khylo.Model.LiveTv;

/// <summary>
/// A tuner's playlist, for choosing which channels to bring into Live TV. Providers often list
/// hundreds of thousands of on-demand titles, so those groups come as counts only.
/// </summary>
public class PlaylistInfo
{
    /// <summary>
    /// Gets or sets every group in the playlist.
    /// </summary>
    public IReadOnlyList<PlaylistGroupInfo> Groups { get; set; } = Array.Empty<PlaylistGroupInfo>();

    /// <summary>
    /// Gets or sets the channels of the live and adult groups (on-demand groups are left out).
    /// </summary>
    public IReadOnlyList<PlaylistChannelInfo> Channels { get; set; } = Array.Empty<PlaylistChannelInfo>();

    /// <summary>
    /// Gets or sets how many entries the whole playlist has.
    /// </summary>
    public int TotalCount { get; set; }
}
