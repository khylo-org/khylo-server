using Khylo.Controller.Entities;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;
using Khylo.Model.Providers;

namespace Khylo.Providers.Books.Isbn
{
    /// <inheritdoc />
    public class IsbnExternalId : IExternalId
    {
        /// <inheritdoc />
        public string ProviderName => "ISBN";

        /// <inheritdoc />
        public string Key => "ISBN";

        /// <inheritdoc />
        public ExternalIdMediaType? Type => null;

        /// <inheritdoc />
        public bool Supports(IHasProviderIds item) => item is Book;
    }
}
