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
using ComicLookupInfo = Khylo.Controller.Providers.ComicInfo;

namespace Khylo.Providers.Books
{
    /// <summary>
    /// Service to manage comic issue metadata.
    /// </summary>
    public class ComicMetadataService : MetadataService<Comic, ComicLookupInfo>
    {
        public ComicMetadataService(
            IServerConfigurationManager serverConfigurationManager,
            ILogger<ComicMetadataService> logger,
            IProviderManager providerManager,
            IFileSystem fileSystem,
            ILibraryManager libraryManager,
            IExternalDataManager externalDataManager,
            IItemRepository itemRepository)
            : base(serverConfigurationManager, logger, providerManager, fileSystem, libraryManager, externalDataManager, itemRepository)
        {
        }

        protected override void MergeData(MetadataResult<Comic> source, MetadataResult<Comic> target, MetadataField[] lockedFields, bool replaceData, bool mergeMetadataSettings)
        {
            base.MergeData(source, target, lockedFields, replaceData, mergeMetadataSettings);

            if (replaceData || string.IsNullOrEmpty(target.Item.SeriesName))
            {
                target.Item.SeriesName = source.Item.SeriesName;
            }

            if (replaceData || string.IsNullOrEmpty(target.Item.IssueNumber))
            {
                target.Item.IssueNumber = source.Item.IssueNumber;
            }

            if (replaceData || !target.Item.Volume.HasValue)
            {
                target.Item.Volume = source.Item.Volume;
            }

            if (replaceData || string.IsNullOrEmpty(target.Item.Publisher))
            {
                target.Item.Publisher = source.Item.Publisher;
            }

            if (replaceData || !target.Item.PageCount.HasValue)
            {
                target.Item.PageCount = source.Item.PageCount;
            }
        }
    }
}
