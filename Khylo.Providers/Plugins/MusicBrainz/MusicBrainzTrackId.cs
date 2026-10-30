using Khylo.Controller.Entities.Audio;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;
using Khylo.Model.Providers;

namespace Khylo.Providers.Plugins.MusicBrainz;

/// <summary>
/// MusicBrainz track id.
/// </summary>
public class MusicBrainzTrackId : IExternalId
{
    /// <inheritdoc />
    public string ProviderName => "MusicBrainz";

    /// <inheritdoc />
    public string Key => MetadataProvider.MusicBrainzTrack.ToString();

    /// <inheritdoc />
    public ExternalIdMediaType? Type => ExternalIdMediaType.Track;

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item) => item is Audio;
}
