using System;

namespace Khylo.Api.Models.HomeProfileDtos;

/// <summary>
/// One home profile.
/// </summary>
public class HomeProfileDto
{
    /// <summary>
    /// Gets or sets the profile's user id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this is the household's owner.
    /// </summary>
    public bool IsOwner { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the caller is signed in as this profile.
    /// </summary>
    public bool IsCurrent { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether switching to this profile needs a PIN.
    /// </summary>
    public bool HasPin { get; set; }

    /// <summary>
    /// Gets or sets the profile picture's tag, if it has one.
    /// </summary>
    public string? PrimaryImageTag { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is a kids profile.
    /// </summary>
    public bool IsKids { get; set; }

    /// <summary>
    /// Gets or sets, for a kids profile, the highest ratings it can watch (e.g. "PG, TV-PG").
    /// </summary>
    public string? RatingLimit { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the caller (a kids profile) can't switch to this
    /// profile until a grown-up gives it a PIN.
    /// </summary>
    public bool NeedsGrownUp { get; set; }
}
