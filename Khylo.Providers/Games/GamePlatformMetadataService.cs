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

namespace Khylo.Providers.Games
{
    /// <summary>
    /// Service to manage video game platform metadata.
    /// </summary>
    public class GamePlatformMetadataService : MetadataService<GamePlatform, ItemLookupInfo>
    {
        public GamePlatformMetadataService(
            IServerConfigurationManager serverConfigurationManager,
            ILogger<GamePlatformMetadataService> logger,
            IProviderManager providerManager,
            IFileSystem fileSystem,
            ILibraryManager libraryManager,
            IExternalDataManager externalDataManager,
            IItemRepository itemRepository)
            : base(serverConfigurationManager, logger, providerManager, fileSystem, libraryManager, externalDataManager, itemRepository)
        {
        }

        protected override void MergeData(MetadataResult<GamePlatform> source, MetadataResult<GamePlatform> target, MetadataField[] lockedFields, bool replaceData, bool mergeMetadataSettings)
        {
            base.MergeData(source, target, lockedFields, replaceData, mergeMetadataSettings);

            if (replaceData || string.IsNullOrEmpty(target.Item.Manufacturer))
            {
                target.Item.Manufacturer = source.Item.Manufacturer;
            }

            if (replaceData || !target.Item.Generation.HasValue)
            {
                target.Item.Generation = source.Item.Generation;
            }
        }
    }
}
