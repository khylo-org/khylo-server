#pragma warning disable CS1591

using System.Collections.Generic;
using Khylo.Common.Configuration;
using Khylo.Model.Configuration;

namespace Khylo.Controller.Library
{
    public class MetadataConfigurationStore : IConfigurationFactory
    {
        public IEnumerable<ConfigurationStore> GetConfigurations()
        {
            return new ConfigurationStore[]
            {
                new ConfigurationStore
                {
                    Key = "metadata",
                    ConfigurationType = typeof(MetadataConfiguration)
                }
            };
        }
    }
}
