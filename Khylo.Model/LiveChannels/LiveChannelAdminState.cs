namespace Khylo.Model.LiveChannels;

/// <summary>
/// Where a channel stands, as shown to administrators.
/// </summary>
public enum LiveChannelAdminState
{
    /// <summary>
    /// Turned on and airing.
    /// </summary>
    OnAir = 0,

    /// <summary>
    /// Turned off by an administrator.
    /// </summary>
    Off = 1,

    /// <summary>
    /// Turned on, but the library doesn't have enough matching content to keep it on the air.
    /// </summary>
    NotEnoughContent = 2,

    /// <summary>
    /// Turned on, but a seasonal channel outside its months.
    /// </summary>
    OutOfSeason = 3
}
