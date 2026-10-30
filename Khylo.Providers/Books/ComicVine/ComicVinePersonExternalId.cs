using Khylo.Controller.Entities;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;
using Khylo.Model.Providers;

namespace Khylo.Providers.Books.ComicVine
{
    /// <inheritdoc />
    public class ComicVinePersonExternalId : IExternalId
    {
        /// <inheritdoc />
        public string ProviderName => "Comic Vine";

        /// <inheritdoc />
        public string Key => "ComicVine";

        /// <inheritdoc />
        public ExternalIdMediaType? Type => ExternalIdMediaType.Person;

        /// <inheritdoc />
        public bool Supports(IHasProviderIds item) => item is Person;
    }
}
