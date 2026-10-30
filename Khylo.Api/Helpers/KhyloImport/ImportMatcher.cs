using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Khylo.Controller.Entities;
using Khylo.Controller.Entities.Audio;
using Khylo.Controller.Entities.Movies;
using Khylo.Controller.Entities.TV;
using Khylo.Controller.Library;
using Khylo.Data.Enums;

namespace Khylo.Api.Helpers.KhyloImport;

/// <summary>
/// Finds the Khylo item for a title from another server: by outside ids (IMDb, TMDB, TVDB,
/// MusicBrainz) first, then by the end of the file path (the same files seen from another folder),
/// then by names (show + season + episode, artist + album + song + length, movie + year). Anything
/// that matches more than one item is left unmatched rather than guessed.
/// </summary>
public sealed class ImportMatcher
{
    private static readonly long _lengthTolerance = TimeSpan.FromSeconds(4).Ticks;

    private readonly Dictionary<string, BaseItem?> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BaseItem?> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BaseItem?> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<BaseItem>> _songs = new(StringComparer.Ordinal);
    private readonly Dictionary<(Guid Series, int Season, int Episode), BaseItem?> _episodes = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ImportMatcher"/> class, reading the whole library once.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    public ImportMatcher(ILibraryManager libraryManager)
    {
        var items = libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes =
            [
                BaseItemKind.Movie, BaseItemKind.Episode, BaseItemKind.Series, BaseItemKind.Audio, BaseItemKind.AudioBook,
                BaseItemKind.MusicVideo, BaseItemKind.MusicAlbum, BaseItemKind.MusicArtist
            ],
            Recursive = true,
            IsVirtualItem = false
        });

        foreach (var item in items)
        {
            var kind = KindOf(item);
            if (kind is null)
            {
                continue;
            }

            foreach (var (provider, value) in item.ProviderIds)
            {
                if (IdKey(provider) is { } key && !string.IsNullOrWhiteSpace(value))
                {
                    Add(_byId, $"{Group(kind.Value)}|{key}|{value.Trim()}", item);
                }
            }

            if (PathKey(item.Path) is { } pathKey && kind is not (ImportEntryKind.Series or ImportEntryKind.Album or ImportEntryKind.Artist))
            {
                Add(_byPath, pathKey, item);
            }

            switch (item)
            {
                case Episode episode when episode.ParentIndexNumber is { } season && episode.IndexNumber is { } number:
                    Add(_episodes, (episode.SeriesId, season, number), item);
                    break;
                case Series:
                    Add(_byName, $"series|{Norm(item.Name)}|{item.ProductionYear}", item);
                    Add(_byName, $"series|{Norm(item.Name)}|", item);
                    break;
                case Movie:
                    Add(_byName, $"movie|{Norm(item.Name)}|{item.ProductionYear}", item);
                    break;
                case MusicAlbum album:
                    Add(_byName, $"album|{Norm(album.AlbumArtist)}|{Norm(album.Name)}", item);
                    break;
                case MusicArtist:
                    Add(_byName, $"artist|{Norm(item.Name)}", item);
                    break;
                case AudioBook book:
                    Add(_byName, $"book|{Norm(book.Name)}", item);
                    break;
                case Audio song:
                    foreach (var artist in song.AlbumArtists.Concat(song.Artists).Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        AddSong($"{Norm(artist)}|{Norm(song.Album)}|{Norm(song.Name)}", item);
                        AddSong($"{Norm(artist)}||{Norm(song.Name)}", item);
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// Finds the Khylo item for an entry.
    /// </summary>
    /// <param name="entry">The title from the other server.</param>
    /// <returns>The item, or null when there's no single confident match.</returns>
    public BaseItem? Match(ImportEntry entry)
    {
        var group = Group(entry.Kind);

        // 1. Outside ids
        foreach (var (provider, value) in entry.Ids)
        {
            if (IdKey(provider) is { } key && _byId.TryGetValue($"{group}|{key}|{value.Trim()}", out var byId) && byId is not null)
            {
                return byId;
            }
        }

        // 2. The same file, wherever the other server keeps it
        if (PathKey(entry.Path) is { } pathKey && _byPath.TryGetValue(pathKey, out var byPath) && byPath is not null)
        {
            return byPath;
        }

        // 3. Names
        return entry.Kind switch
        {
            ImportEntryKind.Episode => MatchEpisode(entry),
            ImportEntryKind.Series => FindSeries(entry.Title, entry.Year, entry.Ids),
            ImportEntryKind.Movie => Lookup($"movie|{Norm(entry.Title)}|{entry.Year}"),
            ImportEntryKind.Album => Lookup($"album|{Norm(entry.Artist)}|{Norm(entry.Title)}"),
            ImportEntryKind.Artist => Lookup($"artist|{Norm(entry.Title)}"),
            ImportEntryKind.AudioBook => Lookup($"book|{Norm(entry.Title)}") ?? MatchSong(entry),
            ImportEntryKind.Track => MatchSong(entry),
            _ => null
        };
    }

    private BaseItem? MatchEpisode(ImportEntry entry)
    {
        if (entry.Season is not { } season || entry.Episode is not { } number)
        {
            return null;
        }

        var series = FindSeries(entry.SeriesTitle, entry.SeriesYear, entry.SeriesIds);
        return series is not null && _episodes.TryGetValue((series.Id, season, number), out var episode) ? episode : null;
    }

    private BaseItem? FindSeries(string? title, int? year, Dictionary<string, string> ids)
    {
        foreach (var (provider, value) in ids)
        {
            if (IdKey(provider) is { } key && _byId.TryGetValue($"series|{key}|{value.Trim()}", out var byId) && byId is not null)
            {
                return byId;
            }
        }

        return Lookup($"series|{Norm(title)}|{year}") ?? Lookup($"series|{Norm(title)}|");
    }

    private BaseItem? MatchSong(ImportEntry entry)
    {
        var candidates = (_songs.GetValueOrDefault($"{Norm(entry.Artist)}|{Norm(entry.Album)}|{Norm(entry.Title)}")
            ?? _songs.GetValueOrDefault($"{Norm(entry.Artist)}||{Norm(entry.Title)}")
            ?? []).Distinct().ToList();

        if (candidates.Count == 1)
        {
            return LengthFits(candidates[0], entry) ? candidates[0] : null;
        }

        // Several versions: the one whose length fits, if only one does
        var fitting = candidates.Where(c => entry.RunTimeTicks is not null && LengthFits(c, entry)).ToList();
        return fitting.Count == 1 ? fitting[0] : null;
    }

    private static bool LengthFits(BaseItem item, ImportEntry entry)
        => entry.RunTimeTicks is not { } length || item.RunTimeTicks is not { } ours || Math.Abs(length - ours) <= _lengthTolerance;

    private BaseItem? Lookup(string key) => _byName.TryGetValue(key, out var item) ? item : null;

    private void AddSong(string key, BaseItem item)
    {
        if (!_songs.TryGetValue(key, out var list))
        {
            _songs[key] = list = [];
        }

        list.Add(item);
    }

    // A key that turns up for two different items is marked ambiguous (null) and never matched
    private static void Add<TKey>(Dictionary<TKey, BaseItem?> map, TKey key, BaseItem item)
        where TKey : notnull
    {
        if (map.TryGetValue(key, out var existing))
        {
            if (existing is not null && !existing.Id.Equals(item.Id))
            {
                map[key] = null;
            }

            return;
        }

        map[key] = item;
    }

    private static ImportEntryKind? KindOf(BaseItem item) => item switch
    {
        Movie => ImportEntryKind.Movie,
        Episode => ImportEntryKind.Episode,
        Series => ImportEntryKind.Series,
        AudioBook => ImportEntryKind.AudioBook,
        Audio => ImportEntryKind.Track,
        MusicVideo => ImportEntryKind.MusicVideo,
        MusicAlbum => ImportEntryKind.Album,
        MusicArtist => ImportEntryKind.Artist,
        _ => null
    };

    // Ids are compared within a group, so a TMDB movie id never matches a TMDB show id
    private static string Group(ImportEntryKind kind) => kind switch
    {
        ImportEntryKind.Movie or ImportEntryKind.MusicVideo => "movie",
        ImportEntryKind.Episode => "episode",
        ImportEntryKind.Series => "series",
        ImportEntryKind.Track or ImportEntryKind.AudioBook => "song",
        ImportEntryKind.Album => "album",
        _ => "artist"
    };

    private static string? IdKey(string provider) => provider.ToLowerInvariant() switch
    {
        "imdb" => "imdb",
        "tmdb" => "tmdb",
        "tvdb" => "tvdb",
        "musicbrainztrack" or "mbtrack" => "mbtrack",
        "musicbrainzrecording" or "mbrecording" => "mbrecording",
        "musicbrainzalbum" or "mbalbum" => "mbalbum",
        "musicbrainzreleasegroup" or "mbreleasegroup" => "mbreleasegroup",
        "musicbrainzartist" or "mbartist" or "musicbrainzalbumartist" or "mbalbumartist" => "mbartist",
        _ => null
    };

    // The last three parts of a path ("Show/Season 1/Episode.mkv"), so the same files match even
    // when the other server sees them from a different drive, share or operating system
    private static string? PathKey(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length < 2 ? null : string.Join('/', parts.TakeLast(Math.Min(3, parts.Length))).ToLowerInvariant();
    }

    // Letters and digits only: case, accents, punctuation, "&"/"and" and a leading "The" don't matter
    private static string Norm(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lower = text.Normalize(NormalizationForm.FormD).ToLowerInvariant().Replace("&", " and ", StringComparison.Ordinal);
        var builder = new StringBuilder(lower.Length);
        foreach (var c in lower)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.IsLetterOrDigit(c) ? c : ' ');
            }
        }

        var words = builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count > 1 && words[0] == "the")
        {
            words.RemoveAt(0);
        }

        return string.Concat(words);
    }
}
