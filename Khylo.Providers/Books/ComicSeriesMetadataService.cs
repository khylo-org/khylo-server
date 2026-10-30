#nullable disable

#pragma warning disable CS1591

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

namespace Khylo.Providers.Books
{
    /// <summary>
    /// Service to manage comic series metadata.
    /// </summary>
    public class ComicSeriesMetadataService : MetadataService<ComicSeries, ItemLookupInfo>
    {
        public ComicSeriesMetadataService(
            IServerConfigurationManager serverConfigurationManager,
            ILogger<ComicSeriesMetadataService> logger,
            IProviderManager providerManager,
            IFileSystem fileSystem,
            ILibraryManager libraryManager,
            IExternalDataManager externalDataManager,
            IItemRepository itemRepository)
            : base(serverConfigurationManager, logger, providerManager, fileSystem, libraryManager, externalDataManager, itemRepository)
        {
        }

        protected override void MergeData(MetadataResult<ComicSeries> source, MetadataResult<ComicSeries> target, MetadataField[] lockedFields, bool replaceData, bool mergeMetadataSettings)
        {
            base.MergeData(source, target, lockedFields, replaceData, mergeMetadataSettings);

            if (replaceData || string.IsNullOrEmpty(target.Item.Publisher))
            {
                target.Item.Publisher = source.Item.Publisher;
            }

            if (replaceData || string.IsNullOrEmpty(target.Item.Status))
            {
                target.Item.Status = source.Item.Status;
            }
        }
    }
}
