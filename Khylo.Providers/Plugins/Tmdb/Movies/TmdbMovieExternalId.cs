using Khylo.Controller.Entities.Movies;
using Khylo.Controller.LiveTv;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;
using Khylo.Model.Providers;

namespace Khylo.Providers.Plugins.Tmdb.Movies
{
    /// <summary>
    /// External id for a TMDb movie.
    /// </summary>
    public class TmdbMovieExternalId : IExternalId
    {
        /// <inheritdoc />
        public string ProviderName => TmdbUtils.ProviderName;

        /// <inheritdoc />
        public string Key => MetadataProvider.Tmdb.ToString();

        /// <inheritdoc />
        public ExternalIdMediaType? Type => ExternalIdMediaType.Movie;

        /// <inheritdoc />
        public bool Supports(IHasProviderIds item)
        {
            // Supports images for tv movies
            if (item is LiveTvProgram tvProgram && tvProgram.IsMovie)
            {
                return true;
            }

            return item is Movie;
        }
    }
}
