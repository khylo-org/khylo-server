using System;
using System.Collections.Generic;

namespace Khylo.Model.LiveChannels;

/// <summary>
/// What to play when tuning in to a Khylo live channel.
/// </summary>
public class LiveChannelTuneResult
{
    /// <summary>
    /// Gets or sets the channel.
    /// </summary>
    public LiveChannelDto Channel { get; set; } = new();

    /// <summary>
    /// Gets or sets the program on the air now.
    /// </summary>
    public LiveChannelProgramDto? Current { get; set; }

    /// <summary>
    /// Gets or sets how far into the current program the channel is, in ticks.
    /// </summary>
    public long StartPositionTicks { get; set; }

    /// <summary>
    /// Gets or sets the programs that follow, to queue after the current one.
    /// </summary>
    public IReadOnlyList<LiveChannelProgramDto> UpNext { get; set; } = Array.Empty<LiveChannelProgramDto>();

    /// <summary>
    /// Gets or sets the server time the position was calculated at (UTC).
    /// </summary>
    public DateTime ServerTimeUtc { get; set; }
}
