#pragma warning disable CS1591

using Khylo.Common.Configuration;
using Khylo.Model.Configuration;

namespace Khylo.XbmcMetadata.Configuration
{
    public static class NfoConfigurationExtension
    {
        public static XbmcMetadataOptions GetNfoConfiguration(this IConfigurationManager manager)
        {
            return manager.GetConfiguration<XbmcMetadataOptions>("xbmcmetadata");
        }
    }
}
