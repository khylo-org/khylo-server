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
    /// Service to manage podcast feed metadata.
    /// </summary>
    public class PodcastMetadataService : MetadataService<Podcast, PodcastInfo>
    {
        public PodcastMetadataService(
            IServerConfigurationManager serverConfigurationManager,
            ILogger<PodcastMetadataService> logger,
            IProviderManager providerManager,
            IFileSystem fileSystem,
            ILibraryManager libraryManager,
            IExternalDataManager externalDataManager,
            IItemRepository itemRepository)
            : base(serverConfigurationManager, logger, providerManager, fileSystem, libraryManager, externalDataManager, itemRepository)
        {
        }

        protected override void MergeData(MetadataResult<Podcast> source, MetadataResult<Podcast> target, MetadataField[] lockedFields, bool replaceData, bool mergeMetadataSettings)
        {
            base.MergeData(source, target, lockedFields, replaceData, mergeMetadataSettings);

            if (replaceData || string.IsNullOrEmpty(target.Item.FeedUrl))
            {
                target.Item.FeedUrl = source.Item.FeedUrl;
            }

            if (replaceData || string.IsNullOrEmpty(target.Item.WebsiteUrl))
            {
                target.Item.WebsiteUrl = source.Item.WebsiteUrl;
            }

            if (replaceData || string.IsNullOrEmpty(target.Item.Author))
            {
                target.Item.Author = source.Item.Author;
            }
        }
    }
}
