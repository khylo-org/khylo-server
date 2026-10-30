using Khylo.Controller.Configuration;
using Khylo.Controller.Entities.Movies;
using Khylo.Controller.IO;
using Khylo.Controller.Library;
using Khylo.Controller.Persistence;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;
using Khylo.Model.IO;
using Khylo.Providers.Manager;
using Microsoft.Extensions.Logging;

namespace Khylo.Providers.Movies;

/// <summary>
/// Service to manage movie metadata.
/// </summary>
public class MovieMetadataService : MetadataService<Movie, MovieInfo>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MovieMetadataService"/> class.
    /// </summary>
    /// <param name="serverConfigurationManager">Instance of the <see cref="IServerConfigurationManager"/>.</param>
    /// <param name="logger">Instance of the <see cref="ILogger"/> interface.</param>
    /// <param name="providerManager">Instance of the <see cref="IProviderManager"/> interface.</param>
    /// <param name="fileSystem">Instance of the <see cref="IFileSystem"/> interface.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="externalDataManager">Instance of the <see cref="IExternalDataManager"/> interface.</param>
    /// <param name="itemRepository">Instance of the <see cref="IItemRepository"/> interface.</param>
    public MovieMetadataService(
        IServerConfigurationManager serverConfigurationManager,
        ILogger<MovieMetadataService> logger,
        IProviderManager providerManager,
        IFileSystem fileSystem,
        ILibraryManager libraryManager,
        IExternalDataManager externalDataManager,
        IItemRepository itemRepository)
        : base(serverConfigurationManager, logger, providerManager, fileSystem, libraryManager, externalDataManager, itemRepository)
    {
    }

    /// <inheritdoc />
    protected override void MergeData(MetadataResult<Movie> source, MetadataResult<Movie> target, MetadataField[] lockedFields, bool replaceData, bool mergeMetadataSettings)
    {
        base.MergeData(source, target, lockedFields, replaceData, mergeMetadataSettings);

        var sourceItem = source.Item;
        var targetItem = target.Item;

        if (replaceData || string.IsNullOrEmpty(targetItem.CollectionName))
        {
            targetItem.CollectionName = sourceItem.CollectionName;
        }
    }
}
