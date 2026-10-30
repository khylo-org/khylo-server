#nullable disable

#pragma warning disable CS1591

using System;
using System.IO;
using Khylo.Controller.Entities;
using Khylo.Controller.Library;
using Khylo.Controller.Resolvers;
using Khylo.Data.Enums;
using Khylo.Naming.Book;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Core.Library.Resolvers.Comics
{
    /// <summary>
    /// Resolves comic series folders containing issues and volumes.
    /// </summary>
    public class ComicSeriesResolver : GenericFolderResolver<ComicSeries>
    {
        private readonly ILogger<ComicSeriesResolver> _logger;

        public ComicSeriesResolver(ILogger<ComicSeriesResolver> logger)
        {
            _logger = logger;
        }

        public override ResolverPriority Priority => ResolverPriority.Second;

        protected override ComicSeries Resolve(ItemResolveArgs args)
        {
            if (!args.IsDirectory)
            {
                return null;
            }

            if (args.HasParent<ComicSeries>())
            {
                return null;
            }

            if (args.Parent is not null && args.Parent.IsRoot)
            {
                return null;
            }

            var collectionType = args.GetCollectionType();

            // Only resolve comic series in comics collections
            if (collectionType == CollectionType.comics)
            {
                var folderName = Path.GetFileName(args.Path);
                var result = BookFileNameParser.Parse(folderName);

                return new ComicSeries
                {
                    Path = args.Path,
                    Name = string.IsNullOrEmpty(result.Name) ? folderName : result.Name,
                    ProductionYear = result.Year
                };
            }

            return null;
        }
    }
}
