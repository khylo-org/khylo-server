using System.Collections.Generic;
using Khylo.Common.Configuration;
using Khylo.Model.Branding;

namespace Khylo.Server.Core.Branding
{
    /// <summary>
    /// A configuration factory for <see cref="BrandingOptions"/>.
    /// </summary>
    public class BrandingConfigurationFactory : IConfigurationFactory
    {
        /// <inheritdoc />
        public IEnumerable<ConfigurationStore> GetConfigurations()
        {
            return new[]
            {
                new ConfigurationStore
                {
                     ConfigurationType = typeof(BrandingOptions),
                     Key = "branding"
                }
            };
        }
    }
}
