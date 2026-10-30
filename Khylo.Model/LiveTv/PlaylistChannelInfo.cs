namespace Khylo.Model.LiveTv;

/// <summary>
/// One entry of a tuner's playlist, for choosing which channels to bring into Live TV.
/// </summary>
public class PlaylistChannelInfo
{
    /// <summary>
    /// Gets or sets the channel id (stable for the playlist's address).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the channel name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the channel number, if the playlist has one.
    /// </summary>
    public string? Number { get; set; }

    /// <summary>
    /// Gets or sets the provider's group (group-title), e.g. "USA | Sports".
    /// </summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the channel logo address.
    /// </summary>
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Gets or sets what the entry is.
    /// </summary>
    public PlaylistChannelKind Kind { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the channel is currently brought into Live TV.
    /// </summary>
    public bool IsIncluded { get; set; }
}
