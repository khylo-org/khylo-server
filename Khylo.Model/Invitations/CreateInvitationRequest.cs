using System;

namespace Khylo.Model.Invitations;

/// <summary>
/// Request payload for creating a new server invitation.
/// </summary>
public class CreateInvitationRequest
{
    /// <summary>
    /// Gets or sets lifetime in hours (default 48 hours).
    /// </summary>
    public int LifetimeHours { get; set; } = 48;

    /// <summary>
    /// Gets or sets maximum number of allowed redemptions.
    /// </summary>
    public int MaxUses { get; set; } = 1;

    /// <summary>
    /// Gets or sets preset allowed library/folder IDs for the invited user.
    /// </summary>
    public Guid[] EnabledFolders { get; set; } = Array.Empty<Guid>();
}
