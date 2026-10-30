using System;
using Khylo.Data.Enums;

namespace Khylo.Model.LiveChannels;

/// <summary>
/// A movie or series in a live channel's rotation.
/// </summary>
public class LiveChannelTitleDto
{
    /// <summary>
    /// Gets or sets the movie or series id.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets the item type.
    /// </summary>
    public BaseItemKind Type { get; set; }

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the production year.
    /// </summary>
    public int? ProductionYear { get; set; }
}
