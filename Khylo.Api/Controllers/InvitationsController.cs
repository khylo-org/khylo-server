using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Khylo.Api.Extensions;
using Khylo.Api.Helpers;
using Khylo.Common.Api;
using Khylo.Common.Extensions;
using Khylo.Controller.Configuration;
using Khylo.Controller.Invitations;
using Khylo.Model.Invitations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Khylo.Api.Controllers;

/// <summary>
/// Khylo user invitations API.
/// </summary>
[Authorize]
[Tags("Invitations")]
public class InvitationsController : BaseKhyloApiController
{
    private const string GuessKey = "invitation-code";

    private readonly IInvitationManager _invitationManager;
    private readonly IServerConfigurationManager _serverConfigurationManager;
    private readonly ILogger<InvitationsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="InvitationsController"/> class.
    /// </summary>
    /// <param name="invitationManager">The invitation manager.</param>
    /// <param name="serverConfigurationManager">The server configuration manager.</param>
    /// <param name="logger">The logger.</param>
    public InvitationsController(
        IInvitationManager invitationManager,
        IServerConfigurationManager serverConfigurationManager,
        ILogger<InvitationsController> logger)
    {
        _invitationManager = invitationManager;
        _serverConfigurationManager = serverConfigurationManager;
        _logger = logger;
    }

    private ObjectResult TooManyTries(TimeSpan wait)
    {
        Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return StatusCode(StatusCodes.Status429TooManyRequests, "Too many tries. Try again in a few minutes.");
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
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public ActionResult<InvitationDetails> GetInvitationDetails([FromRoute, Required] string code)
    {
        // Anyone can ask, so guessing codes is slowed down like guessing passwords
        var remote = HttpContext.GetNormalizedRemoteIP().ToString();
        if (LoginThrottle.RetryAfter(GuessKey, remote) is { } wait)
        {
            return TooManyTries(wait);
        }

        var inv = _invitationManager.GetInvitationByCode(code);
        if (inv == null)
        {
            LoginThrottle.RecordFailure(GuessKey, remote, accountExists: false);
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
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<RedeemInvitationResult>> RedeemInvitation(
        [FromRoute, Required] string code,
        [FromBody, Required] RedeemInvitationRequest request)
    {
        var remote = HttpContext.GetNormalizedRemoteIP().ToString();
        if (LoginThrottle.RetryAfter(GuessKey, remote) is { } wait)
        {
            return TooManyTries(wait);
        }

        try
        {
            var userId = await _invitationManager.RedeemInvitationAsync(code, request.Username, request.Password).ConfigureAwait(false);
            return new RedeemInvitationResult { UserId = userId, Success = true };
        }
        catch (InvalidOperationException ex)
        {
            // These messages are Khylo's own (the code isn't valid, the password isn't acceptable) and safe to show
            LoginThrottle.RecordFailure(GuessKey, remote, accountExists: false);
            return BadRequest(new RedeemInvitationResult { Message = ex.Message });
        }
        catch (ArgumentException)
        {
            return BadRequest(new RedeemInvitationResult { Message = "That name can't be used. Choose another." });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Anything else stays in the log: what went wrong inside isn't for someone without an account
            _logger.LogError(ex, "Couldn't create an account from an invitation");
            return BadRequest(new RedeemInvitationResult { Message = "Couldn't create the account. Try again." });
        }
    }
}
