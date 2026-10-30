using System;
using System.Linq;
using Khylo.Controller.Configuration;
using Khylo.Controller.Entities;
using Khylo.Controller.IO;
using Khylo.Controller.Library;
using Khylo.Controller.Persistence;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;
using Khylo.Model.IO;
using Khylo.Providers.Manager;
using Microsoft.Extensions.Logging;

namespace Khylo.Providers.Books;

/// <summary>
/// Service to manage audiobook metadata.
/// </summary>
public class AudioBookMetadataService : MetadataService<AudioBook, SongInfo>
{
    private static readonly string[] GenericGenres = ["speech", "audiobook", "audiobooks", "audio book", "spoken word", "spoken", "other", "books & spoken"];

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioBookMetadataService"/> class.
    /// </summary>
    /// <param name="serverConfigurationManager">Instance of the <see cref="IServerConfigurationManager"/>.</param>
    /// <param name="logger">Instance of the <see cref="ILogger"/> interface.</param>
    /// <param name="providerManager">Instance of the <see cref="IProviderManager"/> interface.</param>
    /// <param name="fileSystem">Instance of the <see cref="IFileSystem"/> interface.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="externalDataManager">Instance of the <see cref="IExternalDataManager"/> interface.</param>
    /// <param name="itemRepository">Instance of the <see cref="IItemRepository"/> interface.</param>
    public AudioBookMetadataService(
        IServerConfigurationManager serverConfigurationManager,
        ILogger<AudioBookMetadataService> logger,
        IProviderManager providerManager,
        IFileSystem fileSystem,
        ILibraryManager libraryManager,
        IExternalDataManager externalDataManager,
        IItemRepository itemRepository)
        : base(serverConfigurationManager, logger, providerManager, fileSystem, libraryManager, externalDataManager, itemRepository)
    {
    }

    /// <inheritdoc />
    protected override void MergeData(
        MetadataResult<AudioBook> source,
        MetadataResult<AudioBook> target,
        MetadataField[] lockedFields,
        bool replaceData,
        bool mergeMetadataSettings)
    {
        base.MergeData(source, target, lockedFields, replaceData, mergeMetadataSettings);

        var sourceItem = source.Item;
        var targetItem = target.Item;

        // Only take what the source actually has: online book sources don't send the author or book name
        // that the files' tags give, and must not blank them
        if (sourceItem.Artists.Count > 0 && (replaceData || targetItem.Artists.Count == 0))
        {
            targetItem.Artists = sourceItem.Artists;
        }

        if (!string.IsNullOrEmpty(sourceItem.Album) && (replaceData || string.IsNullOrEmpty(targetItem.Album)))
        {
            targetItem.Album = sourceItem.Album;
        }

        if (!string.IsNullOrEmpty(sourceItem.Narrator) && (replaceData || string.IsNullOrEmpty(targetItem.Narrator)))
        {
            targetItem.Narrator = sourceItem.Narrator;
        }

        // Files are often tagged just "Speech" or "Audiobook"; real genres from a book source replace those
        if (sourceItem.Genres.Length > 0 && !lockedFields.Contains(MetadataField.Genres)
            && targetItem.Genres.All(g => GenericGenres.Contains(g, StringComparer.OrdinalIgnoreCase)))
        {
            targetItem.Genres = sourceItem.Genres;
        }
    }
}
