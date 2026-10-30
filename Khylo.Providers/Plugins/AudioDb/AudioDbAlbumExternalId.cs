#pragma warning disable CS1591

using Khylo.Controller.Entities.Audio;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;
using Khylo.Model.Providers;

namespace Khylo.Providers.Plugins.AudioDb
{
    public class AudioDbAlbumExternalId : IExternalId
    {
        /// <inheritdoc />
        public string ProviderName => "TheAudioDb";

        /// <inheritdoc />
        public string Key => MetadataProvider.AudioDbAlbum.ToString();

        /// <inheritdoc />
        public ExternalIdMediaType? Type => null;

        /// <inheritdoc />
        public bool Supports(IHasProviderIds item) => item is MusicAlbum;
    }
}
