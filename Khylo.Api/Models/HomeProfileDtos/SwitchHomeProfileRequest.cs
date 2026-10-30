namespace Khylo.Api.Models.HomeProfileDtos;

/// <summary>
/// A PIN for switching to, or protecting, a profile.
/// </summary>
public class SwitchHomeProfileRequest
{
    /// <summary>
    /// Gets or sets the PIN.
    /// </summary>
    public string? Pin { get; set; }
}
