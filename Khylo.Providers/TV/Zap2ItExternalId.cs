#pragma warning disable CS1591

using Khylo.Controller.Entities.TV;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;
using Khylo.Model.Providers;

namespace Khylo.Providers.TV
{
    public class Zap2ItExternalId : IExternalId
    {
        /// <inheritdoc />
        public string ProviderName => "Zap2It";

        /// <inheritdoc />
        public string Key => MetadataProvider.Zap2It.ToString();

        /// <inheritdoc />
        public ExternalIdMediaType? Type => null;

        /// <inheritdoc />
        public bool Supports(IHasProviderIds item) => item is Series;
    }
}
