using Khylo.Controller.Entities;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;
using Khylo.Model.Providers;

namespace Khylo.Providers.Books.GoogleBooks
{
    /// <inheritdoc />
    public class GoogleBooksExternalId : IExternalId
    {
        /// <inheritdoc />
        public string ProviderName => "Google Books";

        /// <inheritdoc />
        public string Key => "GoogleBooks";

        /// <inheritdoc />
        public ExternalIdMediaType? Type => null;

        /// <inheritdoc />
        public bool Supports(IHasProviderIds item) => item is Book;
    }
}
