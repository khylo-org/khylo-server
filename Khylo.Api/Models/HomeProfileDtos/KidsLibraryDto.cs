using System;

namespace Khylo.Api.Models.HomeProfileDtos;

/// <summary>
/// A library a kids profile can be given.
/// </summary>
public class KidsLibraryDto
{
    /// <summary>
    /// Gets or sets the library id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the library's name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the library's type (movies, tvshows, music, ...).
    /// </summary>
    public string? CollectionType { get; set; }
}
