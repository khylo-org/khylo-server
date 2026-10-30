using System;
using System.Collections.Generic;

namespace Khylo.Server.Implementations.LiveChannels;

/// <summary>
/// The persisted schedule of one channel: the programs already laid out, plus the rotation state
/// needed to continue it exactly where it left off.
/// </summary>
internal sealed class LiveChannelScheduleState
{
    /// <summary>
    /// Gets or sets the scheduled programs, in airing order and back to back.
    /// </summary>
    public List<LiveChannelScheduledProgram> Programs { get; set; } = new();

    /// <summary>
    /// Gets or sets the current rotation deck: unit keys in airing order for this cycle.
    /// </summary>
    public List<string> Deck { get; set; } = new();

    /// <summary>
    /// Gets or sets the position of the next unit in <see cref="Deck"/>.
    /// </summary>
    public int DeckPosition { get; set; }

    /// <summary>
    /// Gets or sets how many decks have been dealt; seeds each shuffle.
    /// </summary>
    public int DeckCycle { get; set; }

    /// <summary>
    /// Gets or sets the last episode aired for each series unit key.
    /// </summary>
    public Dictionary<string, Guid> LastEpisodes { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets a fingerprint of the channel definition the schedule was built for.
    /// </summary>
    public string? DefinitionFingerprint { get; set; }
}
