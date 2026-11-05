using System;
using System.Linq;
using System.Text.Json;
using Khylo.Controller.Entities;
using Khylo.Controller.Entities.Audio;
using Khylo.Controller.Entities.Movies;
using Khylo.Controller.Entities.TV;
using Khylo.Controller.Library;

namespace Khylo.Server.Core.Listening;

/// <summary>
/// Copies what an item is called, and where it belongs, at the moment it is played, so history stays right when the item is
/// later retagged, merged or deleted.
/// </summary>
public static class ListeningSnapshot
{
    /// <summary>
    /// Describes an item for the history, or returns null for something that is not recorded (photos, trailers, theme songs).
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="library">The library manager, to find artist ids.</param>
    /// <returns>A play with the snapshot filled in, and nothing about the person or the time.</returns>
    public static ListeningPlay? Describe(BaseItem item, ILibraryManager library)
    {
        var kind = item switch
        {
            AudioBook => "audiobook",
            PodcastEpisode => "podcast",
            Audio => "music",
            Episode => "episode",
            Movie => "movie",
            _ => null
        };
        if (kind is null || item.IsThemeMedia)
        {
            return null;
        }

        var play = new ListeningPlay
        {
            ItemId = item.Id,
            Kind = kind,
            Title = item.Name,
            DurationSeconds = (item.RunTimeTicks ?? 0) / (double)TimeSpan.TicksPerSecond,
            GenresJson = JsonSerializer.Serialize(item.Genres ?? []),
            Year = item.ProductionYear,
            LibraryId = item.GetTopParent()?.Id
        };

        switch (item)
        {
            case Audio audio:
                var artists = audio.Artists.Where(a => !string.IsNullOrWhiteSpace(a)).ToArray();
                play.Album = audio.Album;
                play.AlbumId = audio.AlbumEntity?.Id;
                play.AlbumArtist = audio.AlbumArtists.FirstOrDefault();
                play.ArtistsJson = JsonSerializer.Serialize(artists);
                play.ArtistIdsJson = JsonSerializer.Serialize(artists.Select(a => library.GetArtist(a)?.Id.ToString("N") ?? string.Empty));
                play.SongKey = ListeningKeys.SongKey(audio.Name, artists.FirstOrDefault() ?? play.AlbumArtist);
                play.AlbumKey = ListeningKeys.AlbumKey(audio.Album, play.AlbumArtist ?? artists.FirstOrDefault());
                break;
            case Episode episode:
                play.SeriesId = episode.SeriesId.Equals(Guid.Empty) ? null : episode.SeriesId;
                play.SeriesName = episode.SeriesName;
                play.SeasonNumber = episode.ParentIndexNumber;
                play.EpisodeNumber = episode.IndexNumber;
                play.SeriesKey = ListeningKeys.SeriesKey(episode.SeriesName);
                break;
            case Movie movie:
                play.MovieKey = ListeningKeys.MovieKey(movie.Name, movie.ProductionYear);
                break;
        }

        return play;
    }
}
