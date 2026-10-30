using System;

namespace Khylo.Model.LiveTv;

/// <summary>
/// Which of a playlist tuner's channels are brought into Live TV.
/// </summary>
public class TunerChannelSelection
{
    /// <summary>
    /// Gets or sets a value indicating whether only the picked channels are imported. When false,
    /// every channel in the playlist is imported.
    /// </summary>
    public bool EnableChannelSelection { get; set; }

    /// <summary>
    /// Gets or sets the groups whose channels are imported (including channels added to them later).
    /// </summary>
    public string[] IncludedGroups { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets channels imported even though their group isn't.
    /// </summary>
    public string[] IncludedChannels { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets channels left out even though their group is imported.
    /// </summary>
    public string[] ExcludedChannels { get; set; } = Array.Empty<string>();
}
