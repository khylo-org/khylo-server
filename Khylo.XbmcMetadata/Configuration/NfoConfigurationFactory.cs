#pragma warning disable CS1591

using System.Collections.Generic;
using Khylo.Common.Configuration;
using Khylo.Model.Configuration;

namespace Khylo.XbmcMetadata.Configuration
{
    public class NfoConfigurationFactory : IConfigurationFactory
    {
        /// <inheritdoc />
        public IEnumerable<ConfigurationStore> GetConfigurations()
        {
            return new[]
            {
                new ConfigurationStore
                {
                    ConfigurationType = typeof(XbmcMetadataOptions),
                    Key = "xbmcmetadata"
                }
            };
        }
    }
}
