using System;
using System.Collections.Generic;

namespace Khylo.Server.Implementations.LiveChannels;

/// <summary>
/// What the rotation picks from: a movie, or a series whose episodes air in order.
/// </summary>
internal sealed class LiveChannelUnit
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LiveChannelUnit"/> class.
    /// </summary>
    /// <param name="key">Stable key, e.g. "m:{id}" or "s:{id}".</param>
    /// <param name="isSeries">Whether this is a series.</param>
    /// <param name="items">The movie, or the series' episodes in airing order.</param>
    public LiveChannelUnit(string key, bool isSeries, IReadOnlyList<LiveChannelUnitItem> items)
    {
        Key = key;
        IsSeries = isSeries;
        Items = items;
    }

    /// <summary>
    /// Gets the stable unit key.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets a value indicating whether this unit is a series.
    /// </summary>
    public bool IsSeries { get; }

    /// <summary>
    /// Gets the playable items, in airing order.
    /// </summary>
    public IReadOnlyList<LiveChannelUnitItem> Items { get; }

    /// <summary>
    /// Gets the movie or series this unit airs.
    /// </summary>
    public Guid TitleId { get; init; }
}
