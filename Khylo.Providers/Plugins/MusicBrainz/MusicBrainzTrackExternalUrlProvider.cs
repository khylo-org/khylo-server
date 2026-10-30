using System.Collections.Generic;
using Khylo.Controller.Entities;
using Khylo.Controller.Entities.Audio;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;

namespace Khylo.Providers.Plugins.MusicBrainz;

/// <summary>
/// External track URLs for MusicBrainz.
/// </summary>
public class MusicBrainzTrackExternalUrlProvider : IExternalUrlProvider
{
    /// <inheritdoc/>
    public string Name => "MusicBrainz Track";

    /// <inheritdoc/>
    public IEnumerable<string> GetExternalUrls(BaseItem item)
    {
        if (item is Audio)
        {
            if (item.TryGetProviderId(MetadataProvider.MusicBrainzTrack, out var externalId))
            {
                yield return Plugin.Instance!.Configuration.Server + $"/track/{externalId}";
            }
        }
    }
}
