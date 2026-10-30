using System.Collections.Generic;
using Khylo.Common.Api;
using Khylo.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Khylo.Api.Controllers;

/// <summary>
/// Whether the storage behind each library can be reached, for the dashboard.
/// </summary>
[Route("Khylo/Storage")]
[Authorize(Policy = Policies.RequiresElevation)]
public class KhyloStorageController : BaseKhyloApiController
{
    /// <summary>
    /// Gets each library location and whether it can be reached.
    /// </summary>
    /// <response code="200">The locations.</response>
    /// <returns>Each location and whether it can be reached.</returns>
    [HttpGet("Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<StorageLocationStatus>> GetStatus() => Ok(StorageHealth.Snapshot());
}
