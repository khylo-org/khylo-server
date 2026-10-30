using System.Collections.Generic;
using Khylo.Model.Entities;

namespace Khylo.Server.Controllers;

/// <summary>Registry of all Khylo media domains.</summary>
public static class KhyloMediaDomains
{
    /// <summary>Gets all predefined media domains.</summary>
    public static readonly IReadOnlyList<KhyloMediaDomainInfo> All =
    [
        new() { Id = "movies", DisplayName = "Movies", Description = "Feature films and short films", Icon = "movie", CollectionType = CollectionTypeOptions.movies },
        new() { Id = "tvshows", DisplayName = "Television", Description = "TV series, seasons, and episodes", Icon = "live_tv", CollectionType = CollectionTypeOptions.tvshows },
        new() { Id = "music", DisplayName = "Music", Description = "Albums, tracks, and artists", Icon = "library_music", CollectionType = CollectionTypeOptions.music },
        new() { Id = "musicvideos", DisplayName = "Music Videos", Description = "Official music video recordings", Icon = "video_library", CollectionType = CollectionTypeOptions.musicvideos },
        new() { Id = "audiobooks", DisplayName = "Audiobooks", Description = "Narrated books with chapter support", Icon = "headphones", CollectionType = CollectionTypeOptions.audiobooks },
        new() { Id = "podcasts", DisplayName = "Podcasts", Description = "Podcast shows and episodes", Icon = "podcast", CollectionType = CollectionTypeOptions.podcasts },
        new() { Id = "books", DisplayName = "Books & Ebooks", Description = "EPub, PDF, and ebook formats", Icon = "menu_book", CollectionType = CollectionTypeOptions.books },
        new() { Id = "comics", DisplayName = "Comics", Description = "CBZ, CB7, CBT comic archives", Icon = "auto_stories", CollectionType = CollectionTypeOptions.comics },
        new() { Id = "photos", DisplayName = "Photos", Description = "JPEG, RAW, HEIC, and image formats", Icon = "photo_library", CollectionType = CollectionTypeOptions.photos },
        new() { Id = "dvds", DisplayName = "DVDs", Description = "DVD ISO images and DVD folders, with their menus", Icon = "album", CollectionType = CollectionTypeOptions.movies },
        new() { Id = "homevideos", DisplayName = "Home Videos", Description = "Personal video recordings", Icon = "videocam", CollectionType = CollectionTypeOptions.homevideos },
        new() { Id = "mixed", DisplayName = "Mixed Media", Description = "Multi-type mixed content library", Icon = "folder_special", CollectionType = CollectionTypeOptions.mixed },
    ];
}
