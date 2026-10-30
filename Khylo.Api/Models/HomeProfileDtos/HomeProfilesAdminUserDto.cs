using System;
using System.Collections.Generic;

namespace Khylo.Api.Models.HomeProfileDtos;

/// <summary>
/// One user who owns a home (or could), for the dashboard.
/// </summary>
public class HomeProfilesAdminUserDto
{
    /// <summary>
    /// Gets or sets the user id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the user's name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the user is an administrator (admins can always add profiles).
    /// </summary>
    public bool IsAdministrator { get; set; }

    /// <summary>
    /// Gets or sets the admin's choice for this user: true, false, or null to follow the server default.
    /// </summary>
    public bool? Allowed { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user can add profiles right now.
    /// </summary>
    public bool CanAddProfiles { get; set; }

    /// <summary>
    /// Gets or sets the profiles in the user's home.
    /// </summary>
    public IReadOnlyList<HomeProfileDto> Profiles { get; set; } = [];
}
