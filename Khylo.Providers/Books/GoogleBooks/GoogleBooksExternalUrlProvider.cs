using System.Collections.Generic;
using Khylo.Controller.Entities;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;

namespace Khylo.Providers.Books.GoogleBooks;

/// <inheritdoc/>
public class GoogleBooksExternalUrlProvider : IExternalUrlProvider
{
    /// <inheritdoc />
    public string Name => "Google Books";

    /// <inheritdoc />
    public IEnumerable<string> GetExternalUrls(BaseItem item)
    {
        if (item.TryGetProviderId("GoogleBooks", out var externalId))
        {
            if (item is Book)
            {
                yield return $"https://books.google.com/books?id={externalId}";
            }
        }
    }
}
