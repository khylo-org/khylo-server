namespace Khylo.Model.LiveTv;

/// <summary>
/// One group (group-title) of a tuner's playlist.
/// </summary>
public class PlaylistGroupInfo
{
    /// <summary>
    /// Gets or sets the group name as the provider writes it.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets what most of the group's entries are.
    /// </summary>
    public PlaylistChannelKind Kind { get; set; }

    /// <summary>
    /// Gets or sets how many entries the group has.
    /// </summary>
    public int ChannelCount { get; set; }
}
