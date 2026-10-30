using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Controller.Library;
using Khylo.Model.Entities;
using Khylo.Model.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Controllers;

/// <summary>
/// Khylo Library Administration API.
/// Provides admin-level access to library health, classification, and domain-specific management.
/// </summary>
[ApiController]
[Route("Khylo/Libraries")]
[Authorize(Policy = "RequiresElevation")]
[Produces("application/json")]
public class KhyloLibraryController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly IDiscImageClassifier _discImageClassifier;
    private readonly ILogger<KhyloLibraryController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="KhyloLibraryController"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="discImageClassifier">The disc image classifier.</param>
    /// <param name="logger">The logger.</param>
    public KhyloLibraryController(
        ILibraryManager libraryManager,
        IDiscImageClassifier discImageClassifier,
        ILogger<KhyloLibraryController> logger)
    {
        _libraryManager = libraryManager;
        _discImageClassifier = discImageClassifier;
        _logger = logger;
    }

    /// <summary>
    /// Gets all libraries with full Khylo administrative detail.
    /// </summary>
    /// <returns>The libraries, including per-path accessibility.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<KhyloLibraryInfo>), StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<KhyloLibraryInfo>> GetLibraries()
    {
        var virtualFolders = _libraryManager.GetVirtualFolders(true);

        var result = virtualFolders.Select(vf =>
        {
            var health = KhyloLibraryHealth.Healthy;
            var pathHealthList = new List<KhyloPathHealth>();
            long failedCount = 0;

            foreach (var loc in vf.Locations ?? [])
            {
                var accessible = Directory.Exists(loc);
                if (!accessible)
                {
                    health = KhyloLibraryHealth.Degraded;
                    failedCount++;
                }

                pathHealthList.Add(new KhyloPathHealth
                {
                    Path = loc,
                    IsAccessible = accessible,
                    InaccessibleReason = accessible ? null : "Path not found or not accessible"
                });
            }

            if (failedCount == (vf.Locations?.Length ?? 0) && failedCount > 0)
            {
                health = KhyloLibraryHealth.Failed;
            }

            CollectionTypeOptions? domain = vf.CollectionType;

            return new KhyloLibraryInfo
            {
                Id = Guid.TryParse(vf.ItemId, out var id) ? id : Guid.Empty,
                Name = vf.Name,
                MediaDomain = domain,
                Locations = vf.Locations?.ToList() ?? [],
                IsEnabled = true,
                ScanState = KhyloLibraryScanState.Idle,
                Health = health,
                ItemCount = 0,
                FailedItemCount = failedCount,
                PathHealth = pathHealthList
            };
        });

        return Ok(result);
    }

    /// <summary>
    /// Gets detailed information for a specific library.
    /// </summary>
    /// <param name="libraryId">The library id.</param>
    /// <returns>The library, or <see cref="NotFoundResult"/> if it does not exist.</returns>
    [HttpGet("{libraryId}")]
    [ProducesResponseType(typeof(KhyloLibraryInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<KhyloLibraryInfo> GetLibrary([FromRoute, Required] Guid libraryId)
    {
        var virtualFolders = _libraryManager.GetVirtualFolders(true);
        var vf = virtualFolders.FirstOrDefault(f => Guid.TryParse(f.ItemId, out var id) && id.Equals(libraryId));

        if (vf is null)
        {
            return NotFound();
        }

        CollectionTypeOptions? domain = vf.CollectionType;

        return Ok(new KhyloLibraryInfo
        {
            Id = libraryId,
            Name = vf.Name,
            MediaDomain = domain,
            Locations = vf.Locations?.ToList() ?? [],
            IsEnabled = true,
            Health = KhyloLibraryHealth.Healthy
        });
    }

    /// <summary>
    /// Classifies a disc image file.
    /// </summary>
    /// <param name="path">The full path of the disc image on the server.</param>
    /// <param name="libraryDomain">Optional collection type of the containing library, used as a hint.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The classification result, or <see cref="BadRequestResult"/> for unsupported input.</returns>
    [HttpPost("ClassifyDisc")]
    [ProducesResponseType(typeof(IsoClassification), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IsoClassification>> ClassifyDiscImage(
        [FromQuery, Required] string path,
        [FromQuery] CollectionTypeOptions? libraryDomain,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return BadRequest("path is required");
        }

        var ext = Path.GetExtension(path);
        if (!_discImageClassifier.CanClassify(ext))
        {
            return BadRequest($"File extension '{ext}' is not a supported disc image format");
        }

        var classification = await _discImageClassifier.ClassifyAsync(path, libraryDomain, cancellationToken)
            .ConfigureAwait(false);

        return Ok(classification);
    }

    /// <summary>
    /// Gets all valid Khylo media domains for library creation.
    /// </summary>
    /// <returns>The predefined media domains.</returns>
    [HttpGet("Domains")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IEnumerable<KhyloMediaDomainInfo>), StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<KhyloMediaDomainInfo>> GetMediaDomains()
    {
        return Ok(KhyloMediaDomains.All);
    }
}
