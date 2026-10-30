using System.ComponentModel.DataAnnotations;

namespace Khylo.Model.Invitations;

/// <summary>
/// Request payload for redeeming a server invitation.
/// </summary>
public class RedeemInvitationRequest
{
    /// <summary>
    /// Gets or sets the desired username.
    /// </summary>
    [Required]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user's password.
    /// </summary>
    [Required]
    public string Password { get; set; } = string.Empty;
}
