using System.Collections.Generic;

namespace Khylo.Api.Models.HomeProfileDtos;

/// <summary>
/// What a kids profile can be limited to: rating levels and libraries.
/// </summary>
public class KidsOptionsDto
{
    /// <summary>
    /// Gets or sets the rating levels, youngest first.
    /// </summary>
    public IReadOnlyList<KidsRatingLevelDto> Levels { get; set; } = [];

    /// <summary>
    /// Gets or sets the server's libraries.
    /// </summary>
    public IReadOnlyList<KidsLibraryDto> Libraries { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the owner's own profile has a PIN. Without one, a kids
    /// profile could switch straight into it, so the owner is asked to set one.
    /// </summary>
    public bool OwnerHasPin { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the person adding the profile has Live TV, so it
    /// can be offered to the kids profile.
    /// </summary>
    public bool LiveTvAvailable { get; set; } = true;
}
