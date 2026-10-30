using System.Collections.Frozen;
using System.Collections.Generic;
using Khylo.Controller.Channels;
using Khylo.Controller.Entities;
using Khylo.Controller.Entities.Audio;
using Khylo.Controller.Entities.Movies;
using Khylo.Controller.Entities.TV;
using Khylo.Controller.LiveTv;
using Khylo.Controller.Persistence;
using Khylo.Controller.Playlists;
using Khylo.Data.Enums;
using Khylo.Server.Core.Playlists;

namespace Khylo.Server.Core.Data;

/// <inheritdoc />
public class ItemTypeLookup : IItemTypeLookup
{
    /// <inheritdoc />
    public IReadOnlyList<string> MusicGenreTypes { get; } = [
         typeof(Audio).FullName!,
         typeof(MusicVideo).FullName!,
         typeof(MusicAlbum).FullName!,
         typeof(MusicArtist).FullName!,
    ];

    /// <inheritdoc />
    public IReadOnlyDictionary<BaseItemKind, string> BaseItemKindNames { get; } = new Dictionary<BaseItemKind, string>()
    {
        { BaseItemKind.AggregateFolder, typeof(AggregateFolder).FullName! },
        { BaseItemKind.Audio, typeof(Audio).FullName! },
        { BaseItemKind.AudioBook, typeof(AudioBook).FullName! },
        { BaseItemKind.BasePluginFolder, typeof(BasePluginFolder).FullName! },
        { BaseItemKind.Book, typeof(Book).FullName! },
        { BaseItemKind.BoxSet, typeof(BoxSet).FullName! },
        { BaseItemKind.Channel, typeof(Channel).FullName! },
        { BaseItemKind.CollectionFolder, typeof(CollectionFolder).FullName! },
        { BaseItemKind.Comic, typeof(Comic).FullName! },
        { BaseItemKind.ComicSeries, typeof(ComicSeries).FullName! },
        { BaseItemKind.Episode, typeof(Episode).FullName! },
        { BaseItemKind.Folder, typeof(Folder).FullName! },
        { BaseItemKind.Game, typeof(Game).FullName! },
        { BaseItemKind.GamePlatform, typeof(GamePlatform).FullName! },
        { BaseItemKind.Genre, typeof(Genre).FullName! },
        { BaseItemKind.Movie, typeof(Movie).FullName! },
        { BaseItemKind.LiveTvChannel, typeof(LiveTvChannel).FullName! },
        { BaseItemKind.LiveTvProgram, typeof(LiveTvProgram).FullName! },
        { BaseItemKind.MusicAlbum, typeof(MusicAlbum).FullName! },
        { BaseItemKind.MusicArtist, typeof(MusicArtist).FullName! },
        { BaseItemKind.MusicGenre, typeof(MusicGenre).FullName! },
        { BaseItemKind.MusicVideo, typeof(MusicVideo).FullName! },
        { BaseItemKind.Person, typeof(Person).FullName! },
        { BaseItemKind.Photo, typeof(Photo).FullName! },
        { BaseItemKind.PhotoAlbum, typeof(PhotoAlbum).FullName! },
        { BaseItemKind.Playlist, typeof(Playlist).FullName! },
        { BaseItemKind.PlaylistsFolder, typeof(PlaylistsFolder).FullName! },
        { BaseItemKind.Podcast, typeof(Podcast).FullName! },
        { BaseItemKind.PodcastEpisode, typeof(PodcastEpisode).FullName! },
        { BaseItemKind.Season, typeof(Season).FullName! },
        { BaseItemKind.Series, typeof(Series).FullName! },
        { BaseItemKind.Studio, typeof(Studio).FullName! },
        { BaseItemKind.Trailer, typeof(Trailer).FullName! },
        { BaseItemKind.TvChannel, typeof(LiveTvChannel).FullName! },
        { BaseItemKind.TvProgram, typeof(LiveTvProgram).FullName! },
        { BaseItemKind.UserRootFolder, typeof(UserRootFolder).FullName! },
        { BaseItemKind.UserView, typeof(UserView).FullName! },
        { BaseItemKind.Video, typeof(Video).FullName! },
        { BaseItemKind.Year, typeof(Year).FullName! }
    }.ToFrozenDictionary();
}
