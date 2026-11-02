using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Api.Helpers;
using Khylo.Api.Models.EnvironmentDtos;
using Khylo.Common.Api;
using Khylo.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Khylo.Api.Controllers;

/// <summary>
/// Network shares (Windows/SMB and NFS) Khylo connects for libraries. Used by the folder picker, including during
/// first-time setup.
/// </summary>
[Route("Khylo/NetworkShares")]
[Authorize(Policy = Policies.FirstTimeSetupOrElevated)]
public class KhyloNetworkSharesController : BaseKhyloApiController
{
    private readonly INetworkShareManager _shares;

    /// <summary>
    /// Initializes a new instance of the <see cref="KhyloNetworkSharesController"/> class.
    /// </summary>
    /// <param name="shares">The network share manager.</param>
    public KhyloNetworkSharesController(INetworkShareManager shares)
    {
        _shares = shares;
    }

    /// <summary>
    /// Gets the connected shares, and whether this server can connect shares at all.
    /// </summary>
    /// <response code="200">The shares.</response>
    /// <returns>The shares and what's possible.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<NetworkSharesResult> GetShares()
    {
        var reason = _shares.WhyCantConnect();
        return new NetworkSharesResult
        {
            CanConnect = reason is null,
            WhyNot = reason,
            Shares = _shares.GetShares()
        };
    }

    /// <summary>
    /// Gets the network drives people have mapped on this Windows computer (X: pointing to \nas\movies), so the folder
    /// picker can offer them. Windows keeps drive letters private to each login, but the addresses behind them can be
    /// connected by this server. Empty on other systems.
    /// </summary>
    /// <response code="200">The mapped drives.</response>
    /// <returns>Each drive with the address behind it.</returns>
    [HttpGet("Mapped")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MappedDriveDto>>> GetMappedDrives()
    {
        return Ok(await WindowsMappedDrives.FindAsync().ConfigureAwait(false));
    }

    /// <summary>
    /// Connects a share and remembers it.
    /// </summary>
    /// <param name="request">The share.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The share is connected.</response>
    /// <response code="400">The share couldn't be connected; the message says why.</response>
    /// <returns>The connected share.</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<NetworkShareInfo>> AddShare([FromBody, Required] AddNetworkShareRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await _shares.AddShareAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (NetworkShareException ex)
        {
            return BadRequest(new NetworkShareError { Message = ex.Message });
        }
    }

    /// <summary>
    /// Disconnects a share and forgets it. Nothing on the share is changed.
    /// </summary>
    /// <param name="id">The share id.</param>
    /// <response code="204">Disconnected.</response>
    /// <response code="404">No such share.</response>
    /// <returns>No content.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> RemoveShare([FromRoute, Required] Guid id)
    {
        return await _shares.RemoveShareAsync(id).ConfigureAwait(false) ? NoContent() : NotFound();
    }

    /// <summary>
    /// The connected shares and whether more can be added.
    /// </summary>
    public class NetworkSharesResult
    {
        /// <summary>Gets or sets a value indicating whether this server can connect shares.</summary>
        public bool CanConnect { get; set; }

        /// <summary>Gets or sets why it can't, in plain words.</summary>
        public string? WhyNot { get; set; }

        /// <summary>Gets or sets the shares.</summary>
        public IReadOnlyList<NetworkShareInfo> Shares { get; set; } = [];
    }

    /// <summary>
    /// Why a share couldn't be connected.
    /// </summary>
    public class NetworkShareError
    {
        /// <summary>Gets or sets the message.</summary>
        public string Message { get; set; } = string.Empty;
    }
}
