using Khylo.Controller.Entities.Audio;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;
using Khylo.Model.Providers;

namespace Khylo.Providers.Plugins.MusicBrainz;

/// <summary>
/// MusicBrainz artist external id.
/// </summary>
public class MusicBrainzArtistExternalId : IExternalId
{
    /// <inheritdoc />
    public string ProviderName => "MusicBrainz";

    /// <inheritdoc />
    public string Key => MetadataProvider.MusicBrainzArtist.ToString();

    /// <inheritdoc />
    public ExternalIdMediaType? Type => ExternalIdMediaType.Artist;

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item) => item is MusicArtist;
}
