using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Api.Extensions;
using Khylo.Api.ModelBinders;
using Khylo.Api.Models.CollectionDtos;
using Khylo.Common.Api;
using Khylo.Controller.Collections;
using Khylo.Controller.Dto;
using Khylo.Controller.Entities;
using Khylo.Controller.Entities.Movies;
using Khylo.Controller.Library;
using Khylo.Data;
using Khylo.Data.Enums;
using Khylo.Database.Implementations.Entities;
using Khylo.Database.Implementations.Enums;
using Khylo.Model.Collections;
using Khylo.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Khylo.Api.Controllers;

/// <summary>
/// The collection controller. Collections are personal: every user can make their own, and only
/// they see and change them. (Collections without an owner, such as ones created automatically
/// from franchise metadata, are managed by administrators and users allowed to manage collections.)
/// </summary>
[Route("Collections")]
[Authorize(Policy = Policies.CollectionManagement)]
public class CollectionController : BaseKhyloApiController
{
    private readonly ICollectionManager _collectionManager;
    private readonly IDtoService _dtoService;
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="CollectionController"/> class.
    /// </summary>
    /// <param name="collectionManager">Instance of <see cref="ICollectionManager"/> interface.</param>
    /// <param name="dtoService">Instance of <see cref="IDtoService"/> interface.</param>
    /// <param name="userManager">Instance of <see cref="IUserManager"/> interface.</param>
    /// <param name="libraryManager">Instance of <see cref="ILibraryManager"/> interface.</param>
    public CollectionController(
        ICollectionManager collectionManager,
        IDtoService dtoService,
        IUserManager userManager,
        ILibraryManager libraryManager)
    {
        _collectionManager = collectionManager;
        _dtoService = dtoService;
        _userManager = userManager;
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Gets the collections the current user can add to: their own, plus collections without an
    /// owner if they may manage those.
    /// </summary>
    /// <param name="itemId">Optional. An item, to report which collections already contain it.</param>
    /// <response code="200">The collections, by name.</response>
    /// <returns>The collections.</returns>
    [HttpGet("Editable")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<EditableCollectionDto>> GetEditableCollections([FromQuery] Guid? itemId)
    {
        var user = _userManager.GetUserById(User.GetUserId());
        if (user is null)
        {
            return Unauthorized();
        }

        var collections = _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = [BaseItemKind.BoxSet],
                Recursive = true
            })
            .OfType<BoxSet>()
            .Where(c => c.CanEdit(user))
            .OrderBy(c => c.SortName ?? c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c => new EditableCollectionDto
            {
                Id = c.Id,
                Name = c.Name,
                IsShared = !c.IsPersonal,
                ItemCount = c.LinkedChildren.Length,
                ContainsItem = itemId.HasValue && c.LinkedChildren.Any(l => l.ItemId.HasValue && l.ItemId.Value.Equals(itemId.Value))
            })
            .ToList();

        return collections;
    }

    /// <summary>
    /// Creates a new collection belonging to the caller; only they see it.
    /// </summary>
    /// <param name="name">The name of the collection.</param>
    /// <param name="ids">Item Ids to add to the collection.</param>
    /// <param name="parentId">Optional. Create the collection within a specific folder.</param>
    /// <param name="isLocked">Whether or not to lock the new collection.</param>
    /// <response code="200">Collection created.</response>
    /// <returns>A <see cref="CollectionCreationOptions"/> with information about the new collection.</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<CollectionCreationResult>> CreateCollection(
        [FromQuery] string? name,
        [FromQuery, ModelBinder(typeof(CommaDelimitedCollectionModelBinder))] string[] ids,
        [FromQuery] Guid? parentId,
        [FromQuery] bool isLocked = false)
    {
        var user = _userManager.GetUserById(User.GetUserId());
        if (user is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest("A collection needs a name.");
        }

        var item = await _collectionManager.CreateCollectionAsync(new CollectionCreationOptions
        {
            IsLocked = isLocked,
            Name = name.Trim(),
            ParentId = parentId,
            ItemIdList = ids,
            UserIds = new[] { user.Id },
            OwnerUserId = user.Id
        }).ConfigureAwait(false);

        var dtoOptions = new DtoOptions();

        var dto = _dtoService.GetBaseItemDto(item, dtoOptions);

        return new CollectionCreationResult
        {
            Id = dto.Id
        };
    }

    /// <summary>
    /// Renames a collection.
    /// </summary>
    /// <param name="collectionId">The collection id.</param>
    /// <param name="name">The new name.</param>
    /// <response code="204">Collection renamed.</response>
    /// <response code="403">The user may not change this collection.</response>
    /// <response code="404">Collection not found.</response>
    /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
    [HttpPost("{collectionId}/Name")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> RenameCollection(
        [FromRoute, Required] Guid collectionId,
        [FromQuery, Required] string name)
    {
        var (collection, error) = GetEditableCollection(collectionId);
        if (collection is null)
        {
            return error!;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest("A collection needs a name.");
        }

        collection.Name = name.Trim();
        collection.ForcedSortName = null;

        // Keep the chosen name when metadata is refreshed from the internet
        if (!collection.LockedFields.Contains(MetadataField.Name))
        {
            collection.LockedFields = [.. collection.LockedFields, MetadataField.Name];
        }

        await collection.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>
    /// Adds items to a collection.
    /// </summary>
    /// <param name="collectionId">The collection id.</param>
    /// <param name="ids">Item ids, comma delimited.</param>
    /// <response code="204">Items added to collection.</response>
    /// <response code="403">The user may not change this collection.</response>
    /// <response code="404">Collection not found.</response>
    /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
    [HttpPost("{collectionId}/Items")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> AddToCollection(
        [FromRoute, Required] Guid collectionId,
        [FromQuery, Required, ModelBinder(typeof(CommaDelimitedCollectionModelBinder))] Guid[] ids)
    {
        var (collection, error) = GetEditableCollection(collectionId);
        if (collection is null)
        {
            return error!;
        }

        await _collectionManager.AddToCollectionAsync(collection.Id, ids).ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>
    /// Removes items from a collection.
    /// </summary>
    /// <param name="collectionId">The collection id.</param>
    /// <param name="ids">Item ids, comma delimited.</param>
    /// <response code="204">Items removed from collection.</response>
    /// <response code="403">The user may not change this collection.</response>
    /// <response code="404">Collection not found.</response>
    /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
    [HttpDelete("{collectionId}/Items")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> RemoveFromCollection(
        [FromRoute, Required] Guid collectionId,
        [FromQuery, Required, ModelBinder(typeof(CommaDelimitedCollectionModelBinder))] Guid[] ids)
    {
        var (collection, error) = GetEditableCollection(collectionId);
        if (collection is null)
        {
            return error!;
        }

        await _collectionManager.RemoveFromCollectionAsync(collection.Id, ids).ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>
    /// Finds a collection the current user may change. Someone else's personal collection reads as
    /// not found, so its existence isn't revealed.
    /// </summary>
    /// <param name="collectionId">The collection id.</param>
    /// <returns>The collection, or the error to return.</returns>
    private (BoxSet? Collection, ActionResult? Error) GetEditableCollection(Guid collectionId)
    {
        var user = _userManager.GetUserById(User.GetUserId());
        if (user is null)
        {
            return (null, Unauthorized());
        }

        if (_libraryManager.GetItemById(collectionId) is not BoxSet collection
            || (collection.IsPersonal && !collection.OwnerUserId.Equals(user.Id)))
        {
            return (null, NotFound());
        }

        return collection.CanEdit(user) ? (collection, null) : (null, Forbid());
    }
}
