using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Api.Helpers.KhyloImport;
using Khylo.Common.Api;
using Khylo.Common.Net;
using Khylo.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Khylo.Api.Controllers;

/// <summary>
/// Brings watch and listening history over from Plex, Khylo or Emby, so people pick up where they
/// left off: watched shows and movies, where they stopped, play counts, favorites and ratings.
/// </summary>
[Route("Khylo/Import")]
[Authorize(Policy = Policies.RequiresElevation)]
public class KhyloImportController : BaseKhyloApiController
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;
    private readonly ILogger<KhyloImportController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="KhyloImportController"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="userDataManager">The user data manager.</param>
    /// <param name="logger">The logger.</param>
    public KhyloImportController(
        IHttpClientFactory httpClientFactory,
        IUserManager userManager,
        ILibraryManager libraryManager,
        IUserDataManager userDataManager,
        ILogger<KhyloImportController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _userManager = userManager;
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
        _logger = logger;
    }

    /// <summary>
    /// Connects to the other server and lists its people.
    /// </summary>
    /// <param name="request">How to reach it.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The other server's users.</response>
    /// <response code="400">The server couldn't be reached or didn't accept the key.</response>
    /// <returns>The users.</returns>
    [HttpPost("Users")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<ImportSourceUser>>> GetSourceUsers([FromBody] ImportConnectRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await ImportJob.GetSourceUsersAsync(request, _httpClientFactory.CreateClient(NamedClient.Default), cancellationToken).ConfigureAwait(false);
        }
        catch (ImportException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Starts an import, or a preview that only counts what would come across.
    /// </summary>
    /// <param name="request">What to import and where.</param>
    /// <response code="202">Started.</response>
    /// <response code="409">One is already running.</response>
    /// <returns>An <see cref="AcceptedResult"/>.</returns>
    [HttpPost("Start")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult Start([FromBody] ImportRunRequest request)
    {
        var started = ImportJob.TryStart(request, _httpClientFactory.CreateClient(NamedClient.Default), _userManager, _libraryManager, _userDataManager, _logger);
        return started ? Accepted() : Conflict("An import is already running.");
    }

    /// <summary>
    /// Gets how the import is going.
    /// </summary>
    /// <response code="200">The status.</response>
    /// <returns>Where the import is now.</returns>
    [HttpGet("Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<ImportStatus> GetStatus() => ImportJob.GetStatus();
}
