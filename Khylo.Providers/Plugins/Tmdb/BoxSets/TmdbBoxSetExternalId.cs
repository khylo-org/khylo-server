using Khylo.Controller.Entities;
using Khylo.Controller.Entities.Movies;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;
using Khylo.Model.Providers;

namespace Khylo.Providers.Plugins.Tmdb.BoxSets
{
    /// <summary>
    /// External id for a TMDb box set.
    /// </summary>
    public class TmdbBoxSetExternalId : IExternalId
    {
        /// <inheritdoc />
        public string ProviderName => TmdbUtils.ProviderName;

        /// <inheritdoc />
        public string Key => MetadataProvider.TmdbCollection.ToString();

        /// <inheritdoc />
        public ExternalIdMediaType? Type => ExternalIdMediaType.BoxSet;

        /// <inheritdoc />
        public bool Supports(IHasProviderIds item)
        {
            return item is Movie || item is MusicVideo || item is Trailer;
        }
    }
}
