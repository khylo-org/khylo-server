#pragma warning disable CS1591

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Khylo.Controller.Dto;
using Khylo.Controller.Entities;
using Khylo.Controller.Entities.Audio;
using Khylo.Controller.Library;
using Khylo.Controller.Playlists;
using Khylo.Data.Enums;
using Khylo.Database.Implementations.Entities;
using Khylo.Database.Implementations.Enums;
using Khylo.Extensions;
using MusicAlbum = Khylo.Controller.Entities.Audio.MusicAlbum;

namespace Khylo.Server.Core.Library
{
    public class MusicManager : IMusicManager
    {
        private readonly ILibraryManager _libraryManager;

        public MusicManager(ILibraryManager libraryManager)
        {
            _libraryManager = libraryManager;
        }

        public IReadOnlyList<BaseItem> GetInstantMixFromSong(Audio item, User? user, DtoOptions dtoOptions)
        {
            // A song without genres mixes like its artist
            if (item.Genres.Length == 0 && item.Artists.FirstOrDefault() is { } artistName
                && _libraryManager.GetArtist(artistName) is { } artist)
            {
                var artistMix = GetInstantMixFromArtist(artist, user, dtoOptions);
                return [item, .. artistMix.Where(i => !i.Id.Equals(item.Id))];
            }

            var instantMixItems = GetInstantMixFromGenres(item.Genres, user, dtoOptions);

            return [item, .. instantMixItems.Where(i => !i.Id.Equals(item.Id))];
        }

        /// <inheritdoc />
        public IReadOnlyList<BaseItem> GetInstantMixFromArtist(MusicArtist artist, User? user, DtoOptions dtoOptions)
        {
            // Khylo: an artist's sound comes from their own songs as well as the artist's genres (artists
            // the online sources don't know have none), and the mix opens with and keeps coming back to
            // the artist's own songs
            var songs = _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = [BaseItemKind.Audio],
                ArtistIds = [artist.Id],
                Recursive = true,
                DtoOptions = dtoOptions
            });

            var genres = artist.Genres.Concat(MainGenres(songs)).DistinctNames();
            return MixWithOwnSongs(songs, GetInstantMixFromGenres(genres, user, dtoOptions));
        }

        public IReadOnlyList<BaseItem> GetInstantMixFromAlbum(MusicAlbum item, User? user, DtoOptions dtoOptions)
        {
            var songs = item.GetRecursiveChildren(i => i is Audio);
            var genres = item.Genres.Concat(MainGenres(songs)).DistinctNames();
            return MixWithOwnSongs(songs, GetInstantMixFromGenres(genres, user, dtoOptions));
        }

        /// <summary>
        /// The genres that make up a real share of a set of songs (one stray "Pop" track among 300 rap songs
        /// shouldn't pull pop into the mix).
        /// </summary>
        private static IEnumerable<string> MainGenres(IReadOnlyCollection<BaseItem> songs)
        {
            if (songs.Count == 0)
            {
                return [];
            }

            return songs
                .SelectMany(s => s.Genres.DistinctNames())
                .GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() >= Math.Max(1, songs.Count / 10))
                .OrderByDescending(g => g.Count())
                .Take(4)
                .Select(g => g.Key);
        }

        /// <summary>
        /// About a third of the mix is the artist's (or album's) own songs, spread through it, starting with one.
        /// </summary>
        private static IReadOnlyList<BaseItem> MixWithOwnSongs(IReadOnlyCollection<BaseItem> ownSongs, IReadOnlyList<BaseItem> genreMix)
        {
            var own = ownSongs.OrderBy(_ => Random.Shared.Next()).Take(60).ToList();
            var ownIds = own.Select(i => i.Id).ToHashSet();
            var others = genreMix.Where(i => !ownIds.Contains(i.Id)).ToList();

            var mix = new List<BaseItem>(own.Count + others.Count);
            int o = 0, r = 0;
            while (mix.Count < 200 && (o < own.Count || r < others.Count))
            {
                if (o < own.Count)
                {
                    mix.Add(own[o++]);
                }

                for (var k = 0; k < 2 && r < others.Count; k++)
                {
                    mix.Add(others[r++]);
                }
            }

            return mix;
        }

        public IReadOnlyList<BaseItem> GetInstantMixFromFolder(Folder item, User? user, DtoOptions dtoOptions)
        {
            var genres = item
               .GetRecursiveChildren(
                user,
                new InternalItemsQuery(user)
                {
                    IncludeItemTypes = [BaseItemKind.Audio],
                    DtoOptions = dtoOptions
                },
                out _)
               .Cast<Audio>()
               .SelectMany(i => i.Genres)
               .Concat(item.Genres)
               .DistinctNames();

            return GetInstantMixFromGenres(genres, user, dtoOptions);
        }

        public IReadOnlyList<BaseItem> GetInstantMixFromPlaylist(Playlist item, User? user, DtoOptions dtoOptions)
        {
            return GetInstantMixFromGenres(item.Genres, user, dtoOptions);
        }

        public IReadOnlyList<BaseItem> GetInstantMixFromGenres(IEnumerable<string> genres, User? user, DtoOptions dtoOptions)
        {
            var genreIds = WithSpellingVariants(genres.DistinctNames().ToList()).Select(i =>
            {
                try
                {
                    return _libraryManager.GetMusicGenre(i).Id;
                }
                catch
                {
                    return Guid.Empty;
                }
            }).Where(i => !i.IsEmpty()).ToArray();

            return GetInstantMixFromGenreIds(genreIds, user, dtoOptions);
        }

        public IReadOnlyList<BaseItem> GetInstantMixFromGenreIds(Guid[] genreIds, User? user, DtoOptions dtoOptions)
        {
            // No genres means nothing to go on; an unfiltered query would be random songs from the whole library
            if (genreIds.Length == 0)
            {
                return [];
            }

            return _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = [BaseItemKind.Audio],
                GenreIds = genreIds,
                Limit = 200,
                OrderBy = [(ItemSortBy.Random, SortOrder.Ascending)],
                DtoOptions = dtoOptions
            });
        }

        /// <summary>
        /// Adds the other spellings of the same genres found in the library: stores tag the same genre
        /// differently ("Hip-Hop/Rap", "Rap/Hip Hop", "Hip Hop"), and a mix should take in all of them.
        /// </summary>
        private List<string> WithSpellingVariants(List<string> genres)
        {
            if (genres.Count == 0)
            {
                return genres;
            }

            var wanted = genres.Select(GenreWords).Where(w => w.Count > 0).ToList();
            var all = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.MusicGenre],
                DtoOptions = new DtoOptions(false)
            });

            var result = new List<string>(genres);
            foreach (var genre in all)
            {
                var words = GenreWords(genre.Name);
                if (words.Count > 0 && wanted.Any(w => w.SetEquals(words)))
                {
                    result.Add(genre.Name);
                }
            }

            return result.DistinctNames().ToList();
        }

        // "Hip-Hop/Rap" -> {hip, hop, rap}; "R&B/Soul" -> {r, b, soul}
        private static HashSet<string> GenreWords(string? name)
            => (name ?? string.Empty)
                .ToLowerInvariant()
                .Split([' ', '-', '/', '&', ',', '+', '.'], StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w != "and" && w != "n")
                .ToHashSet();

        public IReadOnlyList<BaseItem> GetInstantMixFromItem(BaseItem item, User? user, DtoOptions dtoOptions)
        {
            if (item is MusicGenre)
            {
                return GetInstantMixFromGenreIds([item.Id], user, dtoOptions);
            }

            if (item is Playlist playlist)
            {
                return GetInstantMixFromPlaylist(playlist, user, dtoOptions);
            }

            if (item is MusicAlbum album)
            {
                return GetInstantMixFromAlbum(album, user, dtoOptions);
            }

            if (item is MusicArtist artist)
            {
                return GetInstantMixFromArtist(artist, user, dtoOptions);
            }

            if (item is Audio song)
            {
                return GetInstantMixFromSong(song, user, dtoOptions);
            }

            if (item is Folder folder)
            {
                return GetInstantMixFromFolder(folder, user, dtoOptions);
            }

            return new List<BaseItem>();
        }
    }
}
