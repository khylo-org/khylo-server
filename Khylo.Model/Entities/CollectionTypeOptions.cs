#pragma warning disable SA1300 // Lowercase required for backwards compat.

namespace Khylo.Model.Entities;

/// <summary>
/// The collection type options.
/// </summary>
public enum CollectionTypeOptions
{
    /// <summary>
    /// Movies.
    /// </summary>
    movies = 0,

    /// <summary>
    /// TV Shows.
    /// </summary>
    tvshows = 1,

    /// <summary>
    /// Music.
    /// </summary>
    music = 2,

    /// <summary>
    /// Music Videos.
    /// </summary>
    musicvideos = 3,

    /// <summary>
    /// Home Videos (and Photos).
    /// </summary>
    homevideos = 4,

    /// <summary>
    /// Box Sets.
    /// </summary>
    boxsets = 5,

    /// <summary>
    /// Books.
    /// </summary>
    books = 6,

    /// <summary>
    /// Mixed Movies and TV Shows.
    /// </summary>
    mixed = 7,

    /// <summary>
    /// Audiobooks.
    /// </summary>
    audiobooks = 8,

    /// <summary>
    /// Podcasts.
    /// </summary>
    podcasts = 9,

    /// <summary>
    /// Comics.
    /// </summary>
    comics = 10,

    /// <summary>
    /// Games.
    /// </summary>
    games = 11,

    /// <summary>
    /// Photos and image collections.
    /// </summary>
    photos = 12
}
