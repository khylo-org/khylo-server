using Khylo.Controller.Entities.Audio;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;
using Khylo.Model.Providers;

namespace Khylo.Providers.Plugins.MusicBrainz;

/// <summary>
/// MusicBrainz album artist external id.
/// </summary>
public class MusicBrainzAlbumArtistExternalId : IExternalId
{
    /// <inheritdoc />
    public string ProviderName => "MusicBrainz";

    /// <inheritdoc />
    public string Key => MetadataProvider.MusicBrainzAlbumArtist.ToString();

    /// <inheritdoc />
    public ExternalIdMediaType? Type => ExternalIdMediaType.AlbumArtist;

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item) => item is Audio;
}
