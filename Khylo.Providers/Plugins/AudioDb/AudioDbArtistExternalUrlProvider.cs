using System.Collections.Generic;
using Khylo.Controller.Entities;
using Khylo.Controller.Entities.Audio;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;

namespace Khylo.Providers.Plugins.AudioDb;

/// <summary>
/// External artist URLs for AudioDb.
/// </summary>
public class AudioDbArtistExternalUrlProvider : IExternalUrlProvider
{
    /// <inheritdoc/>
    public string Name => "TheAudioDb Artist";

    /// <inheritdoc/>
    public IEnumerable<string> GetExternalUrls(BaseItem item)
    {
        if (item.TryGetProviderId(MetadataProvider.AudioDbArtist, out var externalId))
        {
            var baseUrl = "https://www.theaudiodb.com/";
            switch (item)
            {
                case MusicArtist:
                case Person:
                    yield return baseUrl + $"artist/{externalId}";
                    break;
            }
        }
    }
}
