using System.Collections.Generic;
using Khylo.Common.Configuration;
using Khylo.Model.LiveTv;

namespace Khylo.LiveTv.Configuration;

/// <summary>
/// <see cref="IConfigurationFactory" /> implementation for <see cref="LiveTvOptions" />.
/// </summary>
public class LiveTvConfigurationFactory : IConfigurationFactory
{
    /// <inheritdoc />
    public IEnumerable<ConfigurationStore> GetConfigurations()
    {
        return new[]
        {
            new ConfigurationStore
            {
                ConfigurationType = typeof(LiveTvOptions),
                Key = "livetv"
            }
        };
    }
}
