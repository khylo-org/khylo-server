using System.Collections.Generic;

namespace Khylo.Api.Models.HomeProfileDtos;

/// <summary>
/// One content-rating level a kids profile can be allowed up to, e.g. "PG, TV-PG".
/// </summary>
public class KidsRatingLevelDto
{
    /// <summary>
    /// Gets or sets the rating score (the policy's maximum parental rating).
    /// </summary>
    public int Score { get; set; }

    /// <summary>
    /// Gets or sets the ratings at this level, e.g. PG and TV-PG.
    /// </summary>
    public IReadOnlyList<string> Ratings { get; set; } = [];
}
