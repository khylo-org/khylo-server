using System;

namespace Khylo.Server.Implementations.LiveChannels;

/// <summary>
/// A playable item in a unit.
/// </summary>
internal sealed class LiveChannelUnitItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LiveChannelUnitItem"/> class.
    /// </summary>
    /// <param name="itemId">The library item id.</param>
    /// <param name="runTimeTicks">The runtime in ticks.</param>
    /// <param name="seasonNumber">The season number, for episodes.</param>
    public LiveChannelUnitItem(Guid itemId, long runTimeTicks, int seasonNumber = 0)
    {
        ItemId = itemId;
        RunTimeTicks = runTimeTicks;
        SeasonNumber = seasonNumber;
    }

    /// <summary>
    /// Gets the library item id.
    /// </summary>
    public Guid ItemId { get; }

    /// <summary>
    /// Gets the runtime in ticks.
    /// </summary>
    public long RunTimeTicks { get; }

    /// <summary>
    /// Gets the season number, for episodes.
    /// </summary>
    public int SeasonNumber { get; }
}
