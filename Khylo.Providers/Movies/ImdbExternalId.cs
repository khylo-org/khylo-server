#pragma warning disable CS1591

using Khylo.Controller.Entities;
using Khylo.Controller.Entities.Movies;
using Khylo.Controller.Entities.TV;
using Khylo.Controller.LiveTv;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;
using Khylo.Model.Providers;

namespace Khylo.Providers.Movies
{
    public class ImdbExternalId : IExternalId
    {
        /// <inheritdoc />
        public string ProviderName => "IMDb";

        /// <inheritdoc />
        public string Key => MetadataProvider.Imdb.ToString();

        /// <inheritdoc />
        public ExternalIdMediaType? Type => null;

        /// <inheritdoc />
        public bool Supports(IHasProviderIds item)
        {
            // Supports images for tv movies
            if (item is LiveTvProgram tvProgram && tvProgram.IsMovie)
            {
                return true;
            }

            return item is Movie || item is MusicVideo || item is Series || item is Episode || item is Trailer;
        }
    }
}
