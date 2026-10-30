using System;

namespace Khylo.Model.Invitations;

/// <summary>
/// One person who joined with an invitation.
/// </summary>
public class KhyloInvitationRedemption
{
    /// <summary>
    /// Gets or sets the new user's id.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the name they picked.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when they joined.
    /// </summary>
    public DateTime RedeemedAtUtc { get; set; }
}
