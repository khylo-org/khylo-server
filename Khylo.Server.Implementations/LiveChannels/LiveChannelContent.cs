using System;
using System.Collections.Generic;

namespace Khylo.Server.Implementations.LiveChannels;

/// <summary>
/// The library content one channel airs, worked out from its criteria.
/// </summary>
internal sealed class LiveChannelContent
{
    /// <summary>
    /// Gets the units the rotation picks from, keyed by unit key.
    /// </summary>
    public required IReadOnlyDictionary<string, LiveChannelUnit> Units { get; init; }

    /// <summary>
    /// Gets the libraries the content comes from; viewers need access to all of them.
    /// </summary>
    public required IReadOnlySet<Guid> LibraryIds { get; init; }

    /// <summary>
    /// Gets the highest parental rating score in the content.
    /// </summary>
    public int? MaxRatingScore { get; init; }

    /// <summary>
    /// Gets the total runtime of all content, in ticks.
    /// </summary>
    public long TotalTicks { get; init; }

    /// <summary>
    /// Gets the fewest titles the channel needs to go on the air, so it doesn't loop the same few things.
    /// </summary>
    public required int MinimumTitles { get; init; }

    /// <summary>
    /// Gets the least total runtime the channel needs to go on the air.
    /// </summary>
    public required TimeSpan MinimumRuntime { get; init; }

    /// <summary>
    /// Gets a value indicating whether there is enough content to keep the channel on the air.
    /// </summary>
    public bool IsAvailable => Units.Count >= MinimumTitles && TotalTicks >= MinimumRuntime.Ticks;
}
