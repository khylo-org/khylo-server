using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Khylo.Api.Extensions;
using Khylo.Common.Api;
using Khylo.Controller.Entities;
using Khylo.Controller.Library;
using Khylo.Controller.MediaSegments;
using Khylo.Database.Implementations.Enums;
using Khylo.Model.MediaSegments;
using Khylo.Model.Querying;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Khylo.Api.Controllers;

/// <summary>
/// Media Segments api.
/// </summary>
[Authorize]
[Tags("MediaSegment")]
public class MediaSegmentsController : BaseKhyloApiController
{
    private readonly IMediaSegmentManager _mediaSegmentManager;
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaSegmentsController"/> class.
    /// </summary>
    /// <param name="mediaSegmentManager">MediaSegments Manager.</param>
    /// <param name="libraryManager">The Library manager.</param>
    public MediaSegmentsController(IMediaSegmentManager mediaSegmentManager, ILibraryManager libraryManager)
    {
        _mediaSegmentManager = mediaSegmentManager;
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Gets all media segments based on an itemId.
    /// </summary>
    /// <param name="itemId">The ItemId.</param>
    /// <param name="includeSegmentTypes">Optional filter of requested segment types.</param>
    /// <returns>A list of media segment objects related to the requested itemId.</returns>
    [HttpGet("{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QueryResult<MediaSegmentDto>>> GetItemSegments(
        [FromRoute, Required] Guid itemId,
        [FromQuery] IEnumerable<MediaSegmentType>? includeSegmentTypes = null)
    {
        var item = _libraryManager.GetItemById<BaseItem>(itemId, User.GetUserId());
        if (item is null)
        {
            return NotFound();
        }

        var libraryOptions = _libraryManager.GetLibraryOptions(item);
        var items = await _mediaSegmentManager.GetSegmentsAsync(item, includeSegmentTypes, libraryOptions).ConfigureAwait(false);
        return Ok(new QueryResult<MediaSegmentDto>(items.ToArray()));
    }

    /// <summary>
    /// Creates a media segment for an item.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    /// <param name="segment">The media segment to create.</param>
    /// <returns>The created media segment.</returns>
    [HttpPost("{itemId}")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MediaSegmentDto>> CreateItemSegment(
        [FromRoute, Required] Guid itemId,
        [FromBody, Required] MediaSegmentDto segment)
    {
        var item = _libraryManager.GetItemById<BaseItem>(itemId, User.GetUserId());
        if (item is null)
        {
            return NotFound();
        }

        segment.ItemId = itemId;
        var created = await _mediaSegmentManager.CreateSegmentAsync(segment, "Manual").ConfigureAwait(false);
        return Created($"/MediaSegments/{itemId}", created);
    }

    /// <summary>
    /// Deletes a media segment.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    /// <param name="segmentId">The segment id to delete.</param>
    /// <returns>No content on success.</returns>
    [HttpDelete("{itemId}/{segmentId}")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> DeleteItemSegment(
        [FromRoute, Required] Guid itemId,
        [FromRoute, Required] Guid segmentId)
    {
        await _mediaSegmentManager.DeleteSegmentAsync(segmentId).ConfigureAwait(false);
        return NoContent();
    }
}
