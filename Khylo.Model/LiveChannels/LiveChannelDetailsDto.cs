using System;
using System.Collections.Generic;

namespace Khylo.Model.LiveChannels;

/// <summary>
/// A live channel with its upcoming schedule and the titles in its rotation.
/// </summary>
public class LiveChannelDetailsDto
{
    /// <summary>
    /// Gets or sets the channel with its programs for the next hours.
    /// </summary>
    public LiveChannelDto Channel { get; set; } = new();

    /// <summary>
    /// Gets or sets the titles in the channel's rotation, alphabetically (first ones only).
    /// </summary>
    public IReadOnlyList<LiveChannelTitleDto> Titles { get; set; } = Array.Empty<LiveChannelTitleDto>();

    /// <summary>
    /// Gets or sets how many titles the channel rotates through in total.
    /// </summary>
    public int TitleCount { get; set; }
}
