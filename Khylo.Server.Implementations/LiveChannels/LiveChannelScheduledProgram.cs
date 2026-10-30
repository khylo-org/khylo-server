using System;
using System.Text.Json.Serialization;

namespace Khylo.Server.Implementations.LiveChannels;

/// <summary>
/// A program in a channel schedule.
/// </summary>
internal sealed class LiveChannelScheduledProgram
{
    /// <summary>
    /// Gets or sets the library item.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets the start time (UTC).
    /// </summary>
    public DateTime StartUtc { get; set; }

    /// <summary>
    /// Gets or sets the duration in ticks.
    /// </summary>
    public long DurationTicks { get; set; }

    /// <summary>
    /// Gets the end time (UTC).
    /// </summary>
    [JsonIgnore]
    public DateTime EndUtc => StartUtc.AddTicks(DurationTicks);
}
