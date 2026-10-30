using System.Collections.Generic;
using Khylo.Controller.Entities;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;

namespace Khylo.Providers.Books.ComicVine;

/// <inheritdoc/>
public class ComicVineExternalUrlProvider : IExternalUrlProvider
{
    /// <inheritdoc/>
    public string Name => "Comic Vine";

    /// <inheritdoc />
    public IEnumerable<string> GetExternalUrls(BaseItem item)
    {
        if (item.TryGetProviderId("ComicVine", out var externalId))
        {
            switch (item)
            {
                case Person:
                case Book:
                    yield return $"https://comicvine.gamespot.com/{externalId}";
                    break;
            }
        }
    }
}
