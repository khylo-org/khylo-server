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
    /// Service to manage video game ROM metadata.
    /// </summary>
    public class GameMetadataService : MetadataService<Game, GameInfo>
    {
        public GameMetadataService(
            IServerConfigurationManager serverConfigurationManager,
            ILogger<GameMetadataService> logger,
            IProviderManager providerManager,
            IFileSystem fileSystem,
            ILibraryManager libraryManager,
            IExternalDataManager externalDataManager,
            IItemRepository itemRepository)
            : base(serverConfigurationManager, logger, providerManager, fileSystem, libraryManager, externalDataManager, itemRepository)
        {
        }

        protected override void MergeData(MetadataResult<Game> source, MetadataResult<Game> target, MetadataField[] lockedFields, bool replaceData, bool mergeMetadataSettings)
        {
            base.MergeData(source, target, lockedFields, replaceData, mergeMetadataSettings);

            if (replaceData || string.IsNullOrEmpty(target.Item.Platform))
            {
                target.Item.Platform = source.Item.Platform;
            }

            if (replaceData || string.IsNullOrEmpty(target.Item.Developer))
            {
                target.Item.Developer = source.Item.Developer;
            }

            if (replaceData || string.IsNullOrEmpty(target.Item.Publisher))
            {
                target.Item.Publisher = source.Item.Publisher;
            }

            if (replaceData || !target.Item.Players.HasValue)
            {
                target.Item.Players = source.Item.Players;
            }

            if (replaceData || string.IsNullOrEmpty(target.Item.RomChecksum))
            {
                target.Item.RomChecksum = source.Item.RomChecksum;
            }
        }
    }
}
