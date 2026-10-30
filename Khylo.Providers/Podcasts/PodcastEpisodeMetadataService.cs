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

namespace Khylo.Providers.Podcasts
{
    /// <summary>
    /// Service to manage podcast episode metadata.
    /// </summary>
    public class PodcastEpisodeMetadataService : MetadataService<PodcastEpisode, SongInfo>
    {
        public PodcastEpisodeMetadataService(
            IServerConfigurationManager serverConfigurationManager,
            ILogger<PodcastEpisodeMetadataService> logger,
            IProviderManager providerManager,
            IFileSystem fileSystem,
            ILibraryManager libraryManager,
            IExternalDataManager externalDataManager,
            IItemRepository itemRepository)
            : base(serverConfigurationManager, logger, providerManager, fileSystem, libraryManager, externalDataManager, itemRepository)
        {
        }

        protected override void MergeData(MetadataResult<PodcastEpisode> source, MetadataResult<PodcastEpisode> target, MetadataField[] lockedFields, bool replaceData, bool mergeMetadataSettings)
        {
            base.MergeData(source, target, lockedFields, replaceData, mergeMetadataSettings);

            if (replaceData || !target.Item.EpisodeNumber.HasValue)
            {
                target.Item.EpisodeNumber = source.Item.EpisodeNumber;
            }

            if (replaceData || !target.Item.SeasonNumber.HasValue)
            {
                target.Item.SeasonNumber = source.Item.SeasonNumber;
            }

            if (replaceData || string.IsNullOrEmpty(target.Item.EnclosureUrl))
            {
                target.Item.EnclosureUrl = source.Item.EnclosureUrl;
            }
        }
    }
}
