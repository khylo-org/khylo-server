using System.Collections.Generic;

namespace Khylo.Controller.Mirroring;

/// <summary>
/// A running mirror: the devices that follow one device.
/// </summary>
public class MirrorInfo
{
    /// <summary>Gets or sets the session of the device being followed.</summary>
    public string SourceSessionId { get; set; } = string.Empty;

    /// <summary>Gets or sets the name of the device being followed.</summary>
    public string? SourceName { get; set; }

    /// <summary>Gets or sets what the followed device is playing, if anything.</summary>
    public string? NowPlayingName { get; set; }

    /// <summary>Gets the devices that follow it.</summary>
    public IReadOnlyList<MirrorTargetInfo> Targets { get; init; } = [];
}
