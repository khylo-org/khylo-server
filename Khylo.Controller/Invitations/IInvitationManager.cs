using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Khylo.Model.Invitations;

namespace Khylo.Controller.Invitations;

/// <summary>
/// Service interface for managing server invitations.
/// </summary>
public interface IInvitationManager
{
    /// <summary>
    /// Creates a new invitation.
    /// </summary>
    /// <param name="createdByUserId">The creator user id.</param>
    /// <param name="request">Invitation options.</param>
    /// <returns>The created invitation.</returns>
    KhyloInvitationDto CreateInvitation(Guid createdByUserId, CreateInvitationRequest request);

    /// <summary>
    /// Gets all active and expired invitations.
    /// </summary>
    /// <returns>List of invitations.</returns>
    IReadOnlyList<KhyloInvitationDto> GetInvitations();

    /// <summary>
    /// Gets an invitation by code.
    /// </summary>
    /// <param name="code">The invitation code.</param>
    /// <returns>The invitation if found, otherwise null.</returns>
    KhyloInvitationDto? GetInvitationByCode(string code);

    /// <summary>
    /// Revokes an invitation.
    /// </summary>
    /// <param name="invitationId">The invitation id.</param>
    /// <returns>True if revoked, false if not found.</returns>
    bool RevokeInvitation(Guid invitationId);

    /// <summary>
    /// Deletes a finished (used, expired or revoked) invitation. Accounts made with it are kept.
    /// </summary>
    /// <param name="invitationId">The invitation id.</param>
    /// <returns>True if deleted; false if not found or still active.</returns>
    bool DeleteInvitation(Guid invitationId);

    /// <summary>
    /// Deletes every finished invitation. Accounts made with them are kept.
    /// </summary>
    /// <returns>How many were deleted.</returns>
    int ClearFinishedInvitations();

    /// <summary>
    /// Redeems an invitation and creates the user account.
    /// </summary>
    /// <param name="code">The invitation code.</param>
    /// <param name="username">Username.</param>
    /// <param name="password">Password.</param>
    /// <returns>The created user id.</returns>
    Task<Guid> RedeemInvitationAsync(string code, string username, string password);
}
