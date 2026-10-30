using System.Collections.Generic;

namespace Khylo.Api.Models.HomeProfileDtos;

/// <summary>
/// Home profile settings and every household, for the dashboard.
/// </summary>
public class HomeProfilesAdminDto
{
    /// <summary>
    /// Gets or sets a value indicating whether users may add profiles unless switched off for them.
    /// </summary>
    public bool AllowByDefault { get; set; }

    /// <summary>
    /// Gets or sets how many profiles a home can have (admins aren't limited by this).
    /// </summary>
    public int MaxProfilesPerHome { get; set; }

    /// <summary>
    /// Gets or sets the server's users who own a home (or could), with their profiles.
    /// </summary>
    public IReadOnlyList<HomeProfilesAdminUserDto> Users { get; set; } = [];
}
