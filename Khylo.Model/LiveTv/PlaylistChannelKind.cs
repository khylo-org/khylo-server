namespace Khylo.Model.LiveTv;

/// <summary>
/// What an IPTV playlist entry is. Providers mix live channels with on-demand movies and series,
/// and often adult channels, in one playlist.
/// </summary>
public enum PlaylistChannelKind
{
    /// <summary>
    /// A live channel.
    /// </summary>
    Live = 0,

    /// <summary>
    /// An on-demand movie or episode listed as a channel.
    /// </summary>
    OnDemand = 1,

    /// <summary>
    /// An adult channel.
    /// </summary>
    Adult = 2
}
