using System.Collections.Generic;
using Khylo.Controller.Entities;
using Khylo.Controller.Entities.Audio;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;

namespace Khylo.Providers.Plugins.MusicBrainz;

/// <summary>
/// External artist URLs for MusicBrainz.
/// </summary>
public class MusicBrainzArtistExternalUrlProvider : IExternalUrlProvider
{
    /// <inheritdoc/>
    public string Name => "MusicBrainz Artist";

    /// <inheritdoc/>
    public IEnumerable<string> GetExternalUrls(BaseItem item)
    {
        if (item.TryGetProviderId(MetadataProvider.MusicBrainzArtist, out var externalId))
        {
            switch (item)
            {
                case MusicArtist:
                case Person:
                    yield return Plugin.Instance!.Configuration.Server + $"/artist/{externalId}";

                    break;
            }
        }
    }
}
