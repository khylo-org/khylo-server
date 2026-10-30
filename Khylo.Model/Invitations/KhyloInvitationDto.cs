using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Khylo.Model.Invitations;

/// <summary>
/// Represents a Khylo server user invitation.
/// </summary>
public class KhyloInvitationDto
{
    /// <summary>
    /// Gets or sets the unique invitation id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the invitation code.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the creator user id.
    /// </summary>
    public Guid CreatedByUserId { get; set; }

    /// <summary>
    /// Gets or sets creation timestamp UTC.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets expiration timestamp UTC.
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>
    /// Gets or sets maximum number of allowed redemptions.
    /// </summary>
    public int MaxUses { get; set; } = 1;

    /// <summary>
    /// Gets or sets current redemption count.
    /// </summary>
    public int UseCount { get; set; }

    /// <summary>
    /// Gets or sets preset allowed library/folder IDs.
    /// </summary>
    public Guid[] EnabledFolders { get; set; } = Array.Empty<Guid>();

    /// <summary>
    /// Gets or sets a value indicating whether this invitation is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Gets or sets when an admin revoked the invitation, if they did.
    /// </summary>
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets who joined with this invitation.
    /// </summary>
    public List<KhyloInvitationRedemption> RedeemedBy { get; set; } = [];

    /// <summary>
    /// Gets a value indicating whether this invitation is valid for redemption.
    /// </summary>
    public bool IsValid => IsActive && UseCount < MaxUses && DateTime.UtcNow < ExpiresAtUtc;

    /// <summary>
    /// Gets where the invitation stands: Active, Used, Expired or Revoked.
    /// </summary>
    public string Status
    {
        get
        {
            if (RevokedAtUtc.HasValue)
            {
                return "Revoked";
            }

            if (UseCount >= MaxUses)
            {
                return "Used";
            }

            if (DateTime.UtcNow >= ExpiresAtUtc)
            {
                return "Expired";
            }

            // Switched off before revocations were dated
            return IsActive ? "Active" : "Revoked";
        }
    }

    /// <summary>
    /// Gets when the invitation stopped being usable, or null while it's still active.
    /// </summary>
    [JsonIgnore]
    public DateTime? FinishedAtUtc => Status switch
    {
        "Active" => null,
        "Revoked" => RevokedAtUtc ?? ExpiresAtUtc,
        "Used" => RedeemedBy.Count > 0 ? RedeemedBy.Max(r => r.RedeemedAtUtc) : CreatedAtUtc,
        _ => ExpiresAtUtc
    };
}
