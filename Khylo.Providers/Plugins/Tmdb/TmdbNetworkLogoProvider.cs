using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Controller.LiveChannels;
using Microsoft.Extensions.Logging;

namespace Khylo.Providers.Plugins.Tmdb;

/// <summary>
/// Looks up TV network logos on TMDB through the networks listed on a series.
/// </summary>
public class TmdbNetworkLogoProvider : ILiveChannelLogoProvider
{
    // A few series are enough to find the network on one of them
    private const int MaxSeriesToCheck = 5;

    private readonly TmdbClientManager _tmdbClientManager;
    private readonly ILogger<TmdbNetworkLogoProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbNetworkLogoProvider"/> class.
    /// </summary>
    /// <param name="tmdbClientManager">The TMDB client manager.</param>
    /// <param name="logger">The logger.</param>
    public TmdbNetworkLogoProvider(TmdbClientManager tmdbClientManager, ILogger<TmdbNetworkLogoProvider> logger)
    {
        _tmdbClientManager = tmdbClientManager;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<string?> GetNetworkLogoUrlAsync(IReadOnlyCollection<string> networkNames, IReadOnlyList<string> tmdbSeriesIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(networkNames);
        ArgumentNullException.ThrowIfNull(tmdbSeriesIds);

        foreach (var id in tmdbSeriesIds.Take(MaxSeriesToCheck))
        {
            if (!int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tmdbId))
            {
                continue;
            }

            try
            {
                var series = await _tmdbClientManager.GetSeriesAsync(tmdbId, "en", "en,null", null, cancellationToken).ConfigureAwait(false);
                var network = series?.Networks?.FirstOrDefault(n =>
                    n.Name is not null && networkNames.Contains(n.Name, StringComparer.OrdinalIgnoreCase) && !string.IsNullOrEmpty(n.LogoPath));
                if (network is not null)
                {
                    return _tmdbClientManager.GetLogoUrl(network.LogoPath);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Could not look up networks of TMDB series {TmdbId}", tmdbId);
            }
        }

        return null;
    }
}
