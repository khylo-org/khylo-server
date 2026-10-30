using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Controller.Entities;
using Khylo.Controller.Providers;

namespace Khylo.Providers.Books;

/// <summary>
/// Comic provider.
/// </summary>
public class ComicProvider : ILocalMetadataProvider<Book>, ILocalMetadataProvider<Comic>, IHasItemChangeMonitor
{
    private readonly IEnumerable<IComicProvider> _comicProviders;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComicProvider"/> class.
    /// </summary>
    /// <param name="comicProviders">The list of comic providers.</param>
    public ComicProvider(IEnumerable<IComicProvider> comicProviders)
    {
        _comicProviders = comicProviders;
    }

    /// <inheritdoc />
    public string Name => "Comic Provider";

    /// <inheritdoc />
    public async Task<MetadataResult<Book>> GetMetadata(ItemInfo info, IDirectoryService directoryService, CancellationToken cancellationToken)
    {
        foreach (IComicProvider comicProvider in _comicProviders)
        {
            var metadata = await comicProvider.ReadMetadata(info, directoryService, cancellationToken).ConfigureAwait(false);

            if (metadata.HasMetadata)
            {
                return metadata;
            }
        }

        return new MetadataResult<Book> { HasMetadata = false };
    }

    /// <inheritdoc />
    async Task<MetadataResult<Comic>> ILocalMetadataProvider<Comic>.GetMetadata(ItemInfo info, IDirectoryService directoryService, CancellationToken cancellationToken)
    {
        var bookResult = await GetMetadata(info, directoryService, cancellationToken).ConfigureAwait(false);
        if (!bookResult.HasMetadata)
        {
            return new MetadataResult<Comic> { HasMetadata = false };
        }

        var comic = new Comic
        {
            Name = bookResult.Item.Name,
            Overview = bookResult.Item.Overview,
            ProductionYear = bookResult.Item.ProductionYear,
            PremiereDate = bookResult.Item.PremiereDate,
            IndexNumber = bookResult.Item.IndexNumber,
            IssueNumber = bookResult.Item.IndexNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            SeriesName = bookResult.Item.SeriesName,
            OriginalTitle = bookResult.Item.OriginalTitle,
            IsInMixedFolder = bookResult.Item.IsInMixedFolder,
        };

        foreach (var genre in bookResult.Item.Genres)
        {
            comic.AddGenre(genre);
        }

        comic.SetStudios(bookResult.Item.Studios);
        if (bookResult.Item.Studios.Length > 0)
        {
            comic.Publisher = bookResult.Item.Studios[0];
        }

        return new MetadataResult<Comic>
        {
            HasMetadata = true,
            Item = comic,
            People = bookResult.People,
            ResultLanguage = bookResult.ResultLanguage
        };
    }

    /// <inheritdoc />
    public bool HasChanged(BaseItem item, IDirectoryService directoryService)
    {
        foreach (IComicProvider iComicFileProvider in _comicProviders)
        {
            var fileChanged = iComicFileProvider.HasItemChanged(item);

            if (fileChanged)
            {
                return fileChanged;
            }
        }

        return false;
    }
}
