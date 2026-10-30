using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Khylo.Controller.LiveChannels;

/// <summary>
/// Finds official TV network logos for Khylo network channels, using the metadata provider the
/// library's series came from. Logos are fetched on demand and never bundled.
/// </summary>
public interface ILiveChannelLogoProvider
{
    /// <summary>
    /// Finds the logo of a TV network by looking at series known to air on it.
    /// </summary>
    /// <param name="networkNames">The names the network appears under.</param>
    /// <param name="tmdbSeriesIds">TMDB ids of series that aired on the network.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The logo image URL, or null if none was found.</returns>
    Task<string?> GetNetworkLogoUrlAsync(IReadOnlyCollection<string> networkNames, IReadOnlyList<string> tmdbSeriesIds, CancellationToken cancellationToken);
}
