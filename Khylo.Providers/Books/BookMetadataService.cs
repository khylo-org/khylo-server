using Khylo.Controller.Configuration;
using Khylo.Controller.Entities;
using Khylo.Controller.IO;
using Khylo.Controller.Library;
using Khylo.Controller.Persistence;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;
using Khylo.Model.IO;
using Khylo.Providers.Manager;
using Microsoft.Extensions.Logging;

namespace Khylo.Providers.Books;

/// <summary>
/// Service to manage book metadata.
/// </summary>
public class BookMetadataService : MetadataService<Book, BookInfo>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BookMetadataService"/> class.
    /// </summary>
    /// <param name="serverConfigurationManager">Instance of the <see cref="IServerConfigurationManager"/>.</param>
    /// <param name="logger">Instance of the <see cref="ILogger"/> interface.</param>
    /// <param name="providerManager">Instance of the <see cref="IProviderManager"/> interface.</param>
    /// <param name="fileSystem">Instance of the <see cref="IFileSystem"/> interface.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="externalDataManager">Instance of the <see cref="IExternalDataManager"/> interface.</param>
    /// <param name="itemRepository">Instance of the <see cref="IItemRepository"/> interface.</param>
    public BookMetadataService(
        IServerConfigurationManager serverConfigurationManager,
        ILogger<BookMetadataService> logger,
        IProviderManager providerManager,
        IFileSystem fileSystem,
        ILibraryManager libraryManager,
        IExternalDataManager externalDataManager,
        IItemRepository itemRepository)
        : base(serverConfigurationManager, logger, providerManager, fileSystem, libraryManager, externalDataManager, itemRepository)
    {
    }

    /// <inheritdoc />
    protected override void MergeData(MetadataResult<Book> source, MetadataResult<Book> target, MetadataField[] lockedFields, bool replaceData, bool mergeMetadataSettings)
    {
        base.MergeData(source, target, lockedFields, replaceData, mergeMetadataSettings);

        if (replaceData || string.IsNullOrEmpty(target.Item.SeriesName))
        {
            target.Item.SeriesName = source.Item.SeriesName;
        }

        if (replaceData || !target.Item.PageCount.HasValue)
        {
            target.Item.PageCount = source.Item.PageCount;
        }

        // EPUB dates are often when that edition or file was made (Project Gutenberg's are the 1990s);
        // the earliest year any source gives is when the book first came out
        if (source.Item.ProductionYear is { } year && year > 0 && target.Item.ProductionYear > year)
        {
            target.Item.ProductionYear = year;
        }
    }
}
