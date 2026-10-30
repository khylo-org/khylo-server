using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Controller.Dto;
using Khylo.Controller.Entities;
using Khylo.Controller.Entities.Audio;
using Khylo.Controller.Library;
using Khylo.Data.Enums;
using Khylo.Database.Implementations.Enums;
using Khylo.Model.Configuration;

namespace Khylo.Server.Core.Library.SimilarItems;

/// <summary>
/// Provides similar items for audio tracks.
/// </summary>
public class AudioSimilarItemsProvider : ILocalSimilarItemsProvider<Audio>
{
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioSimilarItemsProvider"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    public AudioSimilarItemsProvider(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    /// <inheritdoc/>
    public string Name => "Local Genre/Tag";

    /// <inheritdoc/>
    public MetadataPluginType Type => MetadataPluginType.LocalSimilarityProvider;

    /// <inheritdoc/>
    public Task<IReadOnlyList<BaseItem>> GetSimilarItemsAsync(Audio item, SimilarItemsQuery query, CancellationToken cancellationToken)
    {
        var internalQuery = new InternalItemsQuery(query.User)
        {
            Genres = item.Genres,
            Tags = item.Tags,
            Limit = query.Limit,
            DtoOptions = query.DtoOptions ?? new DtoOptions(),
            ExcludeItemIds = [.. query.ExcludeItemIds],
            ExcludeArtistIds = [.. query.ExcludeArtistIds],
            IncludeItemTypes = [BaseItemKind.Audio],
            EnableGroupByMetadataKey = false,
            EnableTotalRecordCount = true,
            OrderBy = [(ItemSortBy.Random, SortOrder.Ascending)]
        };

        return Task.FromResult(_libraryManager.GetItemList(internalQuery));
    }
}
