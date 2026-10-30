#nullable disable

#pragma warning disable CS1591

using System;
using System.IO;
using System.Linq;
using Khylo.Controller.Entities;
using Khylo.Controller.Library;
using Khylo.Controller.Resolvers;
using Khylo.Data.Enums;
using Khylo.Extensions;
using Khylo.Naming.Book;

namespace Khylo.Server.Core.Library.Resolvers.Comics
{
    /// <summary>
    /// Resolves comic archives (.cbz, .cbr, .cb7, .cbt, .pdf) into Comic items.
    /// </summary>
    public class ComicResolver : ItemResolver<Comic>
    {
        private readonly string[] _validExtensions = { ".cb7", ".cbr", ".cbt", ".cbz", ".pdf" };

        public override ResolverPriority Priority => ResolverPriority.First;

        protected override Comic Resolve(ItemResolveArgs args)
        {
            var collectionType = args.GetCollectionType();

            // Process items that are in a comics collection or have a ComicSeries parent
            if (collectionType != CollectionType.comics && !args.HasParent<ComicSeries>())
            {
                return null;
            }

            if (args.IsDirectory)
            {
                return GetComicFromDirectory(args);
            }

            var extension = Path.GetExtension(args.Path.AsSpan());

            if (!_validExtensions.Contains(extension, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var result = BookFileNameParser.Parse(Path.GetFileNameWithoutExtension(args.Path));

            var name = !string.IsNullOrEmpty(result.Name)
                ? result.Name
                : (!string.IsNullOrEmpty(result.SeriesName)
                    ? (result.Index.HasValue ? $"{result.SeriesName} #{result.Index.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : result.SeriesName)
                    : Path.GetFileNameWithoutExtension(args.Path));

            var comic = new Comic
            {
                Path = args.Path,
                Name = name,
                IndexNumber = result.Index,
                IssueNumber = result.Index?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                Volume = result.ParentIndex,
                ProductionYear = result.Year,
                SeriesName = result.SeriesName ?? (args.Parent is ComicSeries ? args.Parent.Name : Path.GetFileName(Path.GetDirectoryName(args.Path))),
                IsInMixedFolder = true,
            };

            if (args.Parent is ComicSeries parentSeries)
            {
                comic.SeriesName = parentSeries.Name;
                comic.SeriesId = parentSeries.Id;
            }

            return comic;
        }

        private Comic GetComicFromDirectory(ItemResolveArgs args)
        {
            var comicFiles = args.FileSystemChildren.Where(f =>
            {
                var fileExtension = Path.GetExtension(f.FullName.AsSpan());
                return _validExtensions.Contains(fileExtension, StringComparison.OrdinalIgnoreCase);
            }).ToList();

            if (comicFiles.Count != 1)
            {
                return null;
            }

            var result = BookFileNameParser.Parse(Path.GetFileName(args.Path));

            var name = !string.IsNullOrEmpty(result.Name)
                ? result.Name
                : (!string.IsNullOrEmpty(result.SeriesName)
                    ? (result.Index.HasValue ? $"{result.SeriesName} #{result.Index.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : result.SeriesName)
                    : Path.GetFileName(args.Path));

            var comic = new Comic
            {
                Path = comicFiles[0].FullName,
                Name = name,
                IndexNumber = result.Index,
                IssueNumber = result.Index?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                Volume = result.ParentIndex,
                ProductionYear = result.Year,
                SeriesName = result.SeriesName ?? string.Empty,
            };

            if (args.Parent is ComicSeries parentSeries)
            {
                comic.SeriesName = parentSeries.Name;
                comic.SeriesId = parentSeries.Id;
            }

            return comic;
        }
    }
}
