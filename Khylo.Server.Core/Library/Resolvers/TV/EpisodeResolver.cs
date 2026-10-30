#nullable disable

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Khylo.Controller.Entities.TV;
using Khylo.Controller.Library;
using Khylo.Controller.Providers;
using Khylo.Data.Enums;
using Khylo.Model.Entities;
using Khylo.Naming.Common;
using Khylo.Server.Core.Library;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Core.Library.Resolvers.TV
{
    /// <summary>
    /// Class EpisodeResolver.
    /// </summary>
    public partial class EpisodeResolver : BaseVideoResolver<Episode>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EpisodeResolver"/> class.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="namingOptions">The naming options.</param>
        /// <param name="directoryService">The directory service.</param>
        public EpisodeResolver(ILogger<EpisodeResolver> logger, NamingOptions namingOptions, IDirectoryService directoryService)
            : base(logger, namingOptions, directoryService)
        {
        }

        /// <summary>
        /// Resolves the specified args.
        /// </summary>
        /// <param name="args">The args.</param>
        /// <returns>Episode.</returns>
        protected override Episode Resolve(ItemResolveArgs args)
        {
            var parent = args.Parent;

            if (parent is null)
            {
                return null;
            }

            // Just in case the user decided to nest episodes.
            // Not officially supported but in some cases we can handle it.

            var season = parent as Season ?? parent.GetParents().OfType<Season>().FirstOrDefault();

            // If the parent is a Season or Series and the parent is not an extras folder, then this is an Episode if the VideoResolver returns something
            // Also handle flat tv folders
            if (season is not null
                || args.GetCollectionType() == CollectionType.tvshows
                || args.HasParent<Series>())
            {
                var series = parent as Series ?? parent.GetParents().OfType<Series>().FirstOrDefault();

                // Bonus material kept in its own folder inside the series (e.g. "DVD Extras Vol. 1", "Featurettes",
                // "Bonus Features") must not become episodes. Only folders below the series folder are checked,
                // so a show that is itself named "Extras" or "Bonus Family" still resolves normally.
                if (IsInsideExtrasFolder(args.Path, series?.Path))
                {
                    return null;
                }

                var episode = ResolveVideo<Episode>(args, false);

                // Ignore extras
                if (episode is null || episode.ExtraType is not null)
                {
                    return null;
                }

                if (series is not null)
                {
                    episode.SeriesId = series.Id;
                    episode.SeriesName = series.Name;
                }

                if (season is not null)
                {
                    episode.SeasonId = season.Id;
                    episode.SeasonName = season.Name;
                    if (season.IndexNumber.HasValue)
                    {
                        episode.ParentIndexNumber = season.IndexNumber.Value;
                    }
                }

                // Assume season 1 if there's no season folder and a season number could not be determined
                if (season is null && !episode.ParentIndexNumber.HasValue && (episode.IndexNumber.HasValue || episode.PremiereDate.HasValue))
                {
                    episode.ParentIndexNumber = 1;
                }

                SetProviderIdFromPath(episode, args.Path);

                return episode;
            }

            return null;
        }

        /// <summary>
        /// Checks whether the file sits in a bonus-material folder somewhere between the series folder and the file.
        /// </summary>
        /// <param name="path">The file path.</param>
        /// <param name="seriesPath">The series folder path, if known.</param>
        /// <returns><c>true</c> if a folder below the series folder is an extras/bonus folder.</returns>
        internal static bool IsInsideExtrasFolder(string path, string seriesPath)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(seriesPath))
            {
                return false;
            }

            var seriesDir = Path.TrimEndingDirectorySeparator(seriesPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
            var dir = Path.GetDirectoryName(path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
            while (!string.IsNullOrEmpty(dir)
                && dir.Length > seriesDir.Length
                && dir.StartsWith(seriesDir, StringComparison.OrdinalIgnoreCase))
            {
                if (ExtrasFolderRegex().IsMatch(Path.GetFileName(dir)))
                {
                    return true;
                }

                dir = Path.GetDirectoryName(dir);
            }

            return false;
        }

        [GeneratedRegex(@"^((dvd|blu-?ray|bd)\s+)?(extras?|featurettes?|bonus(\s+(features?|content|material|discs?))?|behind\s+the\s+scenes|special\s+features|deleted\s+scenes)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex ExtrasFolderRegex();

        /// <summary>
        /// Sets provider ids from the episode file name.
        /// </summary>
        /// <param name="item">The episode.</param>
        /// <param name="path">The episode file path.</param>
        private static void SetProviderIdFromPath(Episode item, string path)
        {
            var justName = Path.GetFileNameWithoutExtension(path.AsSpan());

            var imdbId = justName.GetAttributeValue("imdbid");
            item.TrySetProviderId(MetadataProvider.Imdb, imdbId);

            var tvdbId = justName.GetAttributeValue("tvdbid");
            item.TrySetProviderId(MetadataProvider.Tvdb, tvdbId);

            var tvmazeId = justName.GetAttributeValue("tvmazeid");
            item.TrySetProviderId(MetadataProvider.TvMaze, tvmazeId);

            var tmdbId = justName.GetAttributeValue("tmdbid");
            item.TrySetProviderId(MetadataProvider.Tmdb, tmdbId);
        }
    }
}
