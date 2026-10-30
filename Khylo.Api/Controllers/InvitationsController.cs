using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Khylo.Api.Extensions;
using Khylo.Common.Api;
using Khylo.Controller.Configuration;
using Khylo.Controller.Invitations;
using Khylo.Model.Invitations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Khylo.Api.Controllers;

/// <summary>
/// Khylo user invitations API.
/// </summary>
[Authorize]
[Tags("Invitations")]
public class InvitationsController : BaseKhyloApiController
{
    private readonly IInvitationManager _invitationManager;
    private readonly IServerConfigurationManager _serverConfigurationManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="InvitationsController"/> class.
    /// </summary>
    /// <param name="invitationManager">The invitation manager.</param>
    /// <param name="serverConfigurationManager">The server configuration manager.</param>
    public InvitationsController(
        IInvitationManager invitationManager,
        IServerConfigurationManager serverConfigurationManager)
    {
        _invitationManager = invitationManager;
        _serverConfigurationManager = serverConfigurationManager;
    }

    /// <summary>
    /// Creates a new server invitation token.
    /// </summary>
    /// <param name="request">Invitation options.</param>
    /// <returns>The created invitation.</returns>
    [HttpPost]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public ActionResult<KhyloInvitationDto> CreateInvitation([FromBody, Required] CreateInvitationRequest request)
    {
        var invitation = _invitationManager.CreateInvitation(User.GetUserId(), request);
        return Created($"/Invitations/{invitation.Id}", invitation);
    }

    /// <summary>
    /// Gets all invitations.
    /// </summary>
    /// <returns>List of invitations.</returns>
    [HttpGet]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<KhyloInvitationDto>> GetInvitations()
    {
        return Ok(_invitationManager.GetInvitations());
    }

    /// <summary>
    /// Revokes an invitation by id.
    /// </summary>
    /// <param name="invitationId">The invitation id.</param>
    /// <returns>No content on success.</returns>
    [HttpPost("{invitationId:guid}/Revoke")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult RevokeInvitation([FromRoute, Required] Guid invitationId)
    {
        var revoked = _invitationManager.RevokeInvitation(invitationId);
        if (!revoked)
        {
            return NotFound();
        }

        return NoContent();
    }

    /// <summary>
    /// Deletes a finished invitation. Accounts made with it are kept.
    /// </summary>
    /// <param name="invitationId">The invitation id.</param>
    /// <returns>No content on success.</returns>
    [HttpDelete("{invitationId:guid}")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult DeleteInvitation([FromRoute, Required] Guid invitationId)
    {
        return _invitationManager.DeleteInvitation(invitationId) ? NoContent() : NotFound();
    }

    /// <summary>
    /// Deletes every finished invitation. Accounts made with them are kept.
    /// </summary>
    /// <returns>How many were deleted.</returns>
    [HttpDelete("Finished")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<int> ClearFinishedInvitations()
    {
        return _invitationManager.ClearFinishedInvitations();
    }

    /// <summary>
    /// Gets public details of an invitation by code.
    /// </summary>
    /// <param name="code">The invitation code.</param>
    /// <returns>Public invitation metadata.</returns>
    [HttpGet("{code}/Details")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<InvitationDetails> GetInvitationDetails([FromRoute, Required] string code)
    {
        var inv = _invitationManager.GetInvitationByCode(code);
        if (inv == null)
        {
            return NotFound();
        }

        var serverName = _serverConfigurationManager.Configuration.ServerName;
        if (string.IsNullOrEmpty(serverName))
        {
            serverName = "Khylo";
        }

        return new InvitationDetails
        {
            Code = inv.Code,
            ExpiresAtUtc = inv.ExpiresAtUtc,
            IsValid = inv.IsValid,
            ServerName = serverName
        };
    }

    /// <summary>
    /// Redeems an invitation code and creates the new user account.
    /// </summary>
    /// <param name="code">The invitation code.</param>
    /// <param name="request">Redemption details including desired username and password.</param>
    /// <returns>The created user id.</returns>
    [HttpPost("{code}/Redeem")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RedeemInvitationResult>> RedeemInvitation(
        [FromRoute, Required] string code,
        [FromBody, Required] RedeemInvitationRequest request)
    {
        try
        {
            var userId = await _invitationManager.RedeemInvitationAsync(code, request.Username, request.Password).ConfigureAwait(false);
            return new RedeemInvitationResult { UserId = userId, Success = true };
        }
        catch (Exception ex)
        {
            return BadRequest(new RedeemInvitationResult { Message = ex.Message });
        }
    }
}
