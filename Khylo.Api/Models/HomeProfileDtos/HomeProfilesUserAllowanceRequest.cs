namespace Khylo.Api.Models.HomeProfileDtos;

/// <summary>
/// Whether one user may add home profiles.
/// </summary>
public class HomeProfilesUserAllowanceRequest
{
    /// <summary>
    /// Gets or sets true or false, or null to follow the server default.
    /// </summary>
    public bool? Allowed { get; set; }
}
