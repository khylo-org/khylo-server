using Khylo.Controller.Entities.Audio;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;
using Khylo.Model.Providers;

namespace Khylo.Providers.Plugins.MusicBrainz;

/// <summary>
/// MusicBrainz recording id.
/// </summary>
public class MusicBrainzRecordingId : IExternalId
{
    /// <inheritdoc />
    public string ProviderName => "MusicBrainz";

    /// <inheritdoc />
    public string Key => MetadataProvider.MusicBrainzRecording.ToString();

    /// <inheritdoc />
    public ExternalIdMediaType? Type => ExternalIdMediaType.Recording;

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item) => item is Audio;
}
