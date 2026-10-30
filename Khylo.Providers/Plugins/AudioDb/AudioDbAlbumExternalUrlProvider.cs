using System.Collections.Generic;
using Khylo.Controller.Entities;
using Khylo.Controller.Entities.Audio;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;

namespace Khylo.Providers.Plugins.AudioDb;

/// <summary>
/// External artist URLs for AudioDb.
/// </summary>
public class AudioDbAlbumExternalUrlProvider : IExternalUrlProvider
{
    /// <inheritdoc/>
    public string Name => "TheAudioDb Album";

    /// <inheritdoc/>
    public IEnumerable<string> GetExternalUrls(BaseItem item)
    {
        if (item.TryGetProviderId(MetadataProvider.AudioDbAlbum, out var externalId))
        {
            var baseUrl = "https://www.theaudiodb.com/";
            switch (item)
            {
                case MusicAlbum:
                    yield return baseUrl + $"album/{externalId}";
                    break;
            }
        }
    }
}
