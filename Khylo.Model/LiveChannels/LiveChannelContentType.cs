namespace Khylo.Model.LiveChannels;

/// <summary>
/// What a live channel airs. A channel airs either TV series or movies, never both.
/// </summary>
public enum LiveChannelContentType
{
    /// <summary>
    /// TV series, aired in season blocks.
    /// </summary>
    Series = 0,

    /// <summary>
    /// Movies.
    /// </summary>
    Movies = 1
}
