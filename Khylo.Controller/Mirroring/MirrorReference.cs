using System;

namespace Khylo.Controller.Mirroring;

/// <summary>
/// Where the followed device is right now, for a follower that keeps itself in step.
/// </summary>
public class MirrorReference
{
    /// <summary>Gets or sets what the followed device is playing.</summary>
    public Guid ItemId { get; set; }

    /// <summary>Gets or sets the followed device's position at the moment of the answer, in ticks (milliseconds x 10,000).</summary>
    public long PositionTicks { get; set; }

    /// <summary>Gets or sets a value indicating whether the followed device is paused.</summary>
    public bool IsPaused { get; set; }
}
