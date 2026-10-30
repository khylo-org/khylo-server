using System;
using System.Collections.Generic;

namespace Khylo.Api.Models.HomeProfileDtos;

/// <summary>
/// A household's profiles.
/// </summary>
public class HomeProfilesDto
{
    /// <summary>
    /// Gets or sets the profiles, the owner first.
    /// </summary>
    public IReadOnlyList<HomeProfileDto> Profiles { get; set; } = Array.Empty<HomeProfileDto>();

    /// <summary>
    /// Gets or sets a value indicating whether the caller can add and remove profiles.
    /// </summary>
    public bool CanManage { get; set; }
}
