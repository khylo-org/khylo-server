using System.Collections.Generic;
using Khylo.Controller.Entities;
using Khylo.Controller.Entities.TV;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;

namespace Khylo.Providers.Movies;

/// <summary>
/// External URLs for IMDb.
/// </summary>
public class ImdbExternalUrlProvider : IExternalUrlProvider
{
    /// <inheritdoc/>
    public string Name => "IMDb";

    /// <inheritdoc/>
    public IEnumerable<string> GetExternalUrls(BaseItem item)
    {
        var baseUrl = "https://www.imdb.com/";

        if (item is Season season)
        {
            if (season.Series?.TryGetProviderId(MetadataProvider.Imdb, out var seriesImdbId) == true
                && season.IndexNumber.HasValue)
            {
                yield return baseUrl + $"title/{seriesImdbId}/episodes/?season={season.IndexNumber.Value}";
            }

            yield break;
        }

        if (item.TryGetProviderId(MetadataProvider.Imdb, out var externalId))
        {
            if (item is Person)
            {
                yield return baseUrl + $"name/{externalId}";
            }
            else
            {
                yield return baseUrl + $"title/{externalId}";
            }
        }
    }
}
