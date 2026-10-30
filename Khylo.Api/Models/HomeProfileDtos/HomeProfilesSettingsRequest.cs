namespace Khylo.Api.Models.HomeProfileDtos;

/// <summary>
/// Server-wide home profile settings.
/// </summary>
public class HomeProfilesSettingsRequest
{
    /// <summary>
    /// Gets or sets a value indicating whether users may add profiles unless switched off for them.
    /// </summary>
    public bool AllowByDefault { get; set; }

    /// <summary>
    /// Gets or sets how many profiles a home can have.
    /// </summary>
    public int MaxProfilesPerHome { get; set; }
}
