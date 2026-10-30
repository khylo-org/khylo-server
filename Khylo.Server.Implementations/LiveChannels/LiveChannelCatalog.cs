using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Khylo.Model.LiveChannels;

namespace Khylo.Server.Implementations.LiveChannels;

/// <summary>
/// The built-in channel lineup, numbered like a cable lineup: TV from 1, movies from 50, seasonal
/// from 90, networks from 200 and collections from 400. A channel airs TV or movies, never both, and animation stays
/// on its own channels. Channels match each title's own metadata: genres (TMDB/TVDB/IMDb names),
/// TMDB keywords (stored as tags), years, ratings and networks. Channels without enough matching
/// content stay off the air, so bigger libraries simply get more channels.
/// </summary>
internal static class LiveChannelCatalog
{
    /// <summary>
    /// Guide category for TV channels.
    /// </summary>
    public const string TvCategory = "TV";

    /// <summary>
    /// Guide category for movie channels.
    /// </summary>
    public const string MoviesCategory = "Movies";

    /// <summary>
    /// Admin lineup group for seasonal channels (they appear under Movies in the guide).
    /// </summary>
    public const string SeasonalCategory = "Seasonal";

    /// <summary>
    /// Guide category for network channels.
    /// </summary>
    public const string NetworksCategory = "Networks";

    /// <summary>
    /// Guide category for collection channels.
    /// </summary>
    public const string CollectionsCategory = "Collections";

    /// <summary>
    /// Key prefix of network channels.
    /// </summary>
    public const string NetworkKeyPrefix = "network:";

    /// <summary>
    /// Key prefix of collection channels.
    /// </summary>
    public const string CollectionKeyPrefix = "collection:";

    private const int FirstMovieChannel = 50;
    private const int FirstSeasonalChannel = 90;
    private const int FirstNetworkChannel = 200;
    private const int FirstCollectionChannel = 400;
    private const int MaxCollectionChannels = 199;

    // Genre names across TMDB, TVDB and IMDb/OMDb
    private static readonly string[] _animation = ["Animation", "Anime"];
    private static readonly string[] _comedy = ["Comedy", "Sitcom"];
    private static readonly string[] _sciFi = ["Science Fiction", "Sci-Fi & Fantasy", "Sci-Fi", "Science-Fiction", "SciFi"];
    private static readonly string[] _action = ["Action", "Action & Adventure"];
    private static readonly string[] _reality = ["Reality", "Reality-TV", "Reality TV"];
    private static readonly string[] _documentary = ["Documentary"];
    private static readonly string[] _talk = ["Talk", "Talk Show", "Talk-Show"];
    private static readonly string[] _gameShow = ["Game Show", "Game-Show"];
    private static readonly string[] _news = ["News"];
    private static readonly string[] _war = ["War", "War & Politics"];
    private static readonly string[] _music = ["Music", "Musical"];
    private static readonly string[] _kids = ["Family", "Kids", "Children", "Animation"];
    private static readonly string[] _thriller = ["Thriller", "Suspense"];

    // Live-action channels leave out animation; scripted TV channels also leave out unscripted genres
    private static readonly string[] _notAnimated = _animation;
    private static readonly string[] _scripted = [.. _animation, .. _reality, .. _documentary, .. _talk, .. _gameShow, .. _news];

    /// <summary>
    /// Creates the fixed built-in channel definitions: category channels and seasonal channels.
    /// Category channels are listed in priority order, most specific first; each title airs on the
    /// first one it matches (see <see cref="LiveChannelCriteria.Exclusive"/>).
    /// </summary>
    /// <returns>The definitions.</returns>
    public static IReadOnlyList<LiveChannelDefinition> Create()
    {
        var tv = new List<LiveChannelDefinition>
        {
            // Animation first, so cartoons never land on live-action channels
            Tv("kids-tv", "Kids TV", "Shows rated TV-Y7 and under.", new() { MaxOfficialRating = "TV-Y7", ExcludeGenres = [.. _reality, .. _documentary, .. _news] }),
            Tv("anime", "Anime", "Japanese animated series.", new() { AnyOf = ["Anime", "anime"], RequiredGenres = ["Animation"] }),
            Tv("animation", "Animation", "Animated comedies and adventures for grown-ups.", new() { Genres = _animation }),

            // Unscripted
            Tv("reality", "Reality", "Reality and unscripted series.", new() { Genres = _reality }),
            Tv("game-shows", "Game Shows", "Game and quiz shows.", new() { Genres = _gameShow }),
            Tv("talk", "Talk & Late Night", "Talk shows and late night.", new() { Genres = _talk }),
            Tv("docuseries", "Docuseries", "Documentary and true crime series.", new() { Genres = _documentary }),

            // Scripted, most specific first
            Tv("classic-tv", "Classic TV", "Live-action favorites that first aired before 2000 and wrapped up by 2009.", new() { MaxYear = 1999, EndedByYear = 2009, ExcludeGenres = _scripted }),
            Tv("sitcoms", "Sitcoms", "Half-hour live-action comedies.", new() { Genres = _comedy, ExcludeGenres = _scripted, MaxEpisodeMinutes = 35 }),
            Tv("superhero-tv", "Superhero TV", "Caped crusaders and comic book series.", new() { AnyOf = ["Superhero", "superhero", "dc comics", "marvel comics"], ExcludeGenres = _scripted }),
            Tv("horror-tv", "Horror & Supernatural", "Horror and supernatural series.", new() { AnyOf = ["Horror", "horror", "supernatural horror", "zombie", "vampire", "haunted house"], ExcludeGenres = _scripted }),
            Tv("scifi-tv", "Sci-Fi & Fantasy", "Science fiction and fantasy series.", new() { Genres = [.. _sciFi, "Fantasy"], ExcludeGenres = _scripted }),
            Tv("medical-tv", "Medical Dramas", "Hospital and medical series.", new() { AnyOf = ["Medical", "medical drama", "hospital", "doctor", "surgeon"], ExcludeGenres = _scripted }),
            Tv("legal-tv", "Legal Dramas", "Lawyers, courtrooms and legal series.", new() { AnyOf = ["Legal", "legal drama", "lawyer", "courtroom", "law firm"], ExcludeGenres = _scripted }),
            Tv("procedurals", "Police Procedurals", "Cops, detectives and investigators.", new() { AnyOf = ["police procedural", "procedural", "police detective", "detective", "homicide detective"], ExcludeGenres = _scripted }),
            Tv("crime-tv", "Crime & Mystery", "Crime and mystery series.", new() { Genres = ["Crime", "Mystery"], ExcludeGenres = _scripted }),
            Tv("action-tv", "Action & Adventure", "Action and adventure series.", new() { Genres = [.. _action, "Adventure"], ExcludeGenres = _scripted }),
            Tv("war-tv", "War & Politics", "War, military and political series.", new() { Genres = _war, ExcludeGenres = _scripted }),
            Tv("western-tv", "Western Series", "Western series.", new() { Genres = ["Western"], ExcludeGenres = _scripted }),
            Tv("teen-tv", "Teen TV", "High school and coming-of-age series.", new() { AnyOf = ["Teen", "teen drama", "high school", "teenager", "coming of age"], ExcludeGenres = _scripted }),
            Tv("family-tv", "Family TV", "Live-action family series rated TV-PG and under.", new() { Genres = ["Family"], MaxOfficialRating = "TV-PG", ExcludeGenres = _scripted }),
            Tv("comedy-tv", "Comedy TV", "Hour-long comedies and dramedies.", new() { Genres = _comedy, ExcludeGenres = _scripted }),
            Tv("drama-tv", "Drama", "Drama series.", new() { Genres = ["Drama"], ExcludeGenres = _scripted })
        };

        var movies = new List<LiveChannelDefinition>
        {
            // Animation and family first, so cartoons never land on live-action channels
            Movie("family-movies", "Family Movies", "Family and animated movies rated PG and under.", new() { Genres = _kids, MaxOfficialRating = "PG", ExcludeGenres = ["Horror"] }),
            Movie("anime-movies", "Anime Movies", "Japanese animated films.", new() { AnyOf = ["Anime", "anime"], RequiredGenres = ["Animation"] }),
            Movie("animated-movies", "Animated Movies", "Animated movies for older audiences.", new() { Genres = _animation }),
            Movie("documentaries", "Documentaries", "Documentary films.", new() { Genres = _documentary }),
            Movie("classic-movies", "Classic Movies", "Movies released before 1970.", new() { MaxYear = 1969 }),

            // Distinct themes before the broad genres they belong to
            Movie("superhero-movies", "Superheroes", "Superhero movies.", new() { AnyOf = ["Superhero", "superhero", "marvel cinematic universe (mcu)", "dc extended universe (dceu)", "dc universe (dcu)", "superhero team"] }),
            Movie("spy-movies", "Spies & Espionage", "Secret agents and espionage.", new() { AnyOf = ["Spy", "spy", "espionage", "secret agent", "mi6", "james bond"] }),
            Movie("heist-movies", "Heists & Capers", "Robberies, cons and capers.", new() { AnyOf = ["heist", "bank robbery", "heist movie", "con artist"] }),
            Movie("war-movies", "War Movies", "War movies.", new() { Genres = _war }),
            Movie("westerns", "Westerns", "Western movies.", new() { Genres = ["Western"] }),
            Movie("music-movies", "Music & Musicals", "Music movies and musicals.", new() { Genres = _music }),
            Movie("sports-movies", "Sports Movies", "Sports dramas and comedies.", new() { AnyOf = ["Sport", "Sports", "sports", "sport", "boxing", "football (soccer)", "american football", "basketball", "baseball", "ice hockey"] }),
            Movie("true-story-movies", "True Stories", "Biopics and movies based on real events.", new() { AnyOf = ["Biography", "biography", "biopic", "based on real person", "based on true story"] }),

            // Broad genres
            Movie("horror-movies", "Horror Movies", "Horror movies.", new() { Genres = ["Horror"] }),
            Movie("scifi-movies", "Sci-Fi Movies", "Science fiction movies.", new() { Genres = _sciFi }),
            Movie("fantasy-movies", "Fantasy Movies", "Fantasy movies.", new() { Genres = ["Fantasy"] }),
            Movie("romcoms", "Rom-Coms", "Romantic comedies.", new() { RequiredGenres = ["Romance", "Comedy"] }),
            Movie("action-movies", "Action Movies", "Action movies.", new() { Genres = _action }),
            Movie("adventure-movies", "Adventure Movies", "Adventure movies.", new() { Genres = ["Adventure"] }),
            Movie("comedy-movies", "Comedy Movies", "Comedy movies.", new() { Genres = _comedy }),
            Movie("crime-movies", "Crime Movies", "Crime and gangster movies.", new() { Genres = ["Crime"] }),
            Movie("thriller-movies", "Thrillers & Mystery", "Thrillers, suspense and mysteries.", new() { Genres = [.. _thriller, "Mystery"] }),
            Movie("romance-movies", "Romance", "Romance movies.", new() { Genres = ["Romance"] }),
            Movie("drama-movies", "Drama Movies", "Drama movies.", new() { Genres = ["Drama"] })
        };

        // Seasonal channels draw from the whole library (on top of a title's own channel), and are
        // only on the air in their months
        var seasonal = new List<LiveChannelDefinition>
        {
            Movie("christmas-movies", "Christmas Movies", "Holiday favorites, every November and December.", new() { AnyOf = ["christmas", "santa claus", "christmas eve", "christmas party"], ExcludeGenres = ["Horror"], ActiveMonths = [11, 12] }),
            Movie("halloween-movies", "31 Nights of Halloween", "Halloween horror and spooky favorites, all October.", new() { AnyOf = ["halloween", "trick or treating"], ActiveMonths = [10] }),
            Movie("valentines-movies", "Valentine's Romance", "Love stories, every February.", new() { Genres = ["Romance"], ActiveMonths = [2] })
        };

        foreach (var channel in tv.Concat(movies))
        {
            channel.Criteria.Exclusive = true;
        }

        Number(tv, 1);
        Number(movies, FirstMovieChannel);
        Number(seasonal, FirstSeasonalChannel);
        return [.. tv, .. movies, .. seasonal];
    }

    /// <summary>
    /// Creates a channel for each known TV network that has shows in the library.
    /// </summary>
    /// <param name="isPresent">Whether any series in the library lists one of a network's names.</param>
    /// <returns>The network channels, numbered by their place in <see cref="LiveChannelNetworks.All"/>.</returns>
    public static IReadOnlyList<LiveChannelDefinition> CreateNetworkChannels(Func<string[], bool> isPresent)
    {
        var channels = new List<LiveChannelDefinition>();
        var networks = LiveChannelNetworks.All;
        for (var i = 0; i < networks.Count; i++)
        {
            var (name, aliases, airsAnimation) = networks[i];
            if (!isPresent(aliases))
            {
                continue;
            }

            var criteria = new LiveChannelCriteria
            {
                ContentType = LiveChannelContentType.Series,
                Studios = aliases,
                ExcludeGenres = airsAnimation ? [.. _news] : [.. _notAnimated, .. _news]
            };
            var channel = Define(NetworkKeyPrefix + name.ToLowerInvariant(), name, $"Shows from {name}.", criteria);
            channel.Category = NetworksCategory;
            channel.Number = FirstNetworkChannel + i;
            channels.Add(channel);
        }

        return channels;
    }

    /// <summary>
    /// Creates a channel per movie collection. Collections belong to people, so each person's
    /// collection channels are theirs alone and are numbered from 400 within their own lineup.
    /// </summary>
    /// <param name="collections">The collections: id, name, owner and the movies in it.</param>
    /// <returns>The collection channels, numbered alphabetically from 400 per owner.</returns>
    public static IReadOnlyList<LiveChannelDefinition> CreateCollectionChannels(IEnumerable<(Guid Id, string Name, Guid? OwnerUserId, Guid[] MovieIds)> collections)
    {
        var result = new List<LiveChannelDefinition>();
        foreach (var lineup in collections.GroupBy(c => c.OwnerUserId))
        {
            var channels = new List<LiveChannelDefinition>();
            foreach (var (id, name, ownerUserId, movieIds) in lineup)
            {
                var criteria = new LiveChannelCriteria
                {
                    ContentType = LiveChannelContentType.Movies,
                    ItemIds = movieIds
                };
                var channel = Define(CollectionKeyPrefix + id.ToString("N"), name, $"The {name} movies, back to back.", criteria);
                channel.Category = CollectionsCategory;
                channel.OwnerUserId = ownerUserId;
                channels.Add(channel);
            }

            channels.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            if (channels.Count > MaxCollectionChannels)
            {
                channels.RemoveRange(MaxCollectionChannels, channels.Count - MaxCollectionChannels);
            }

            Number(channels, FirstCollectionChannel);
            result.AddRange(channels);
        }

        return result;
    }

    /// <summary>
    /// Gets the stable id of a built-in channel.
    /// </summary>
    /// <param name="autoKey">The built-in channel key.</param>
    /// <returns>The channel id.</returns>
    public static Guid GetId(string autoKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("khylo-live-channel:" + autoKey));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static void Number(List<LiveChannelDefinition> channels, int first)
    {
        for (var i = 0; i < channels.Count; i++)
        {
            channels[i].Number = first + i;
        }
    }

    private static LiveChannelDefinition Tv(string key, string name, string description, LiveChannelCriteria criteria)
    {
        criteria.ContentType = LiveChannelContentType.Series;
        var channel = Define(key, name, description, criteria);
        channel.Category = TvCategory;
        return channel;
    }

    private static LiveChannelDefinition Movie(string key, string name, string description, LiveChannelCriteria criteria)
    {
        criteria.ContentType = LiveChannelContentType.Movies;
        var channel = Define(key, name, description, criteria);
        channel.Category = MoviesCategory;
        return channel;
    }

    private static LiveChannelDefinition Define(string key, string name, string description, LiveChannelCriteria criteria)
        => new()
        {
            Id = GetId(key),
            AutoKey = key,
            Name = name,
            Description = description,
            Criteria = criteria
        };
}
