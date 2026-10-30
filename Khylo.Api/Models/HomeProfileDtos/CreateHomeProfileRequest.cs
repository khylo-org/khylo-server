using System;
using System.Collections.Generic;

namespace Khylo.Api.Models.HomeProfileDtos;

/// <summary>
/// A new home profile.
/// </summary>
public class CreateHomeProfileRequest
{
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets an optional PIN.
    /// </summary>
    public string? Pin { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is a kids profile, limited to the content below.
    /// Only an administrator can change those limits afterwards.
    /// </summary>
    public bool IsKids { get; set; }

    /// <summary>
    /// Gets or sets the highest rating score a kids profile may watch.
    /// </summary>
    public int? MaxParentalRating { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a kids profile may watch unrated (NR) titles.
    /// </summary>
    public bool AllowUnrated { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a kids profile may watch Live TV.
    /// </summary>
    public bool AllowLiveTv { get; set; }

    /// <summary>
    /// Gets or sets the libraries a kids profile can see; empty means all of them.
    /// </summary>
    public IReadOnlyList<Guid>? Libraries { get; set; }

    /// <summary>
    /// Gets or sets a PIN for the owner's own profile, set when adding the first kids profile.
    /// </summary>
    public string? OwnerPin { get; set; }
}
