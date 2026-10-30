using System.Collections.Generic;
using Khylo.Controller.Entities;
using Khylo.Controller.Entities.Audio;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;

namespace Khylo.Providers.Plugins.MusicBrainz;

/// <summary>
/// External album URLs for MusicBrainz.
/// </summary>
public class MusicBrainzAlbumExternalUrlProvider : IExternalUrlProvider
{
    /// <inheritdoc/>
    public string Name => "MusicBrainz Album";

    /// <inheritdoc/>
    public IEnumerable<string> GetExternalUrls(BaseItem item)
    {
        if (item is MusicAlbum)
        {
            if (item.TryGetProviderId(MetadataProvider.MusicBrainzAlbum, out var externalId))
            {
                yield return Plugin.Instance!.Configuration.Server + $"/release/{externalId}";
            }
        }
    }
}
