using System;
using Khylo.Controller.Channels;
using Khylo.Controller.Configuration;
using Khylo.Controller.Entities;
using Khylo.Controller.Library;
using Khylo.Extensions;
using Khylo.Model.Configuration;

namespace Khylo.Controller.BaseItemManager
{
    /// <inheritdoc />
    public class BaseItemManager : IBaseItemManager
    {
        private readonly IServerConfigurationManager _serverConfigurationManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="BaseItemManager"/> class.
        /// </summary>
        /// <param name="serverConfigurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
        public BaseItemManager(IServerConfigurationManager serverConfigurationManager)
        {
            _serverConfigurationManager = serverConfigurationManager;
        }

        /// <inheritdoc />
        public bool IsMetadataFetcherEnabled(BaseItem baseItem, TypeOptions? libraryTypeOptions, string name)
        {
            if (baseItem is Channel)
            {
                // Hack alert.
                return true;
            }

            if (baseItem.SourceType == SourceType.Channel)
            {
                // Hack alert.
                return !baseItem.EnableMediaSourceDisplay;
            }

            // Khylo: a library that never picked any online sources falls back to the server's defaults,
            // so anything without local metadata still gets filled in from online
            if (libraryTypeOptions is not null && libraryTypeOptions.MetadataFetchers.Length > 0)
            {
                return libraryTypeOptions.MetadataFetchers.Contains(name, StringComparison.OrdinalIgnoreCase);
            }

            var itemConfig = _serverConfigurationManager.GetMetadataOptionsForType(baseItem.GetType().Name);
            return itemConfig is null || !itemConfig.DisabledMetadataFetchers.Contains(name, StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        public bool IsImageFetcherEnabled(BaseItem baseItem, TypeOptions? libraryTypeOptions, string name)
        {
            if (baseItem is Channel)
            {
                // Hack alert.
                return true;
            }

            if (baseItem.SourceType == SourceType.Channel)
            {
                // Hack alert.
                return !baseItem.EnableMediaSourceDisplay;
            }

            // Khylo: same fallback for artwork
            if (libraryTypeOptions is not null && libraryTypeOptions.ImageFetchers.Length > 0)
            {
                return libraryTypeOptions.ImageFetchers.Contains(name, StringComparison.OrdinalIgnoreCase);
            }

            var itemConfig = _serverConfigurationManager.GetMetadataOptionsForType(baseItem.GetType().Name);
            return itemConfig is null || !itemConfig.DisabledImageFetchers.Contains(name, StringComparison.OrdinalIgnoreCase);
        }
    }
}
