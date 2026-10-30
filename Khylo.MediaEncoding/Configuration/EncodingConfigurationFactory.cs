#pragma warning disable CS1591

using System.Collections.Generic;
using Khylo.Common.Configuration;

namespace Khylo.MediaEncoding.Configuration
{
    public class EncodingConfigurationFactory : IConfigurationFactory
    {
        public IEnumerable<ConfigurationStore> GetConfigurations()
        {
            return new[]
            {
                new EncodingConfigurationStore()
            };
        }
    }
}
