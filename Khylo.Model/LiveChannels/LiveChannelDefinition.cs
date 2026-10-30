using System;

namespace Khylo.Model.LiveChannels;

/// <summary>
/// A Khylo live channel: a continuously scheduled "TV station" built from the library.
/// </summary>
public class LiveChannelDefinition
{
    /// <summary>
    /// Gets or sets the channel id. Auto channels use an id derived from <see cref="AutoKey"/>.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the key of the built-in channel this was generated from, or null for custom channels.
    /// </summary>
    public string? AutoKey { get; set; }

    /// <summary>
    /// Gets or sets the channel number shown in the guide.
    /// </summary>
    public int Number { get; set; }

    /// <summary>
    /// Gets or sets the channel name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the guide category, e.g. TV, Movies, Networks or Collections.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a short description of the channel.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the channel is on the air.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the user a personal channel (built from their own collection) belongs to; only
    /// they see it. Null for channels everyone can watch.
    /// </summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>
    /// Gets or sets the content the channel draws from.
    /// </summary>
    public LiveChannelCriteria Criteria { get; set; } = new();
}
