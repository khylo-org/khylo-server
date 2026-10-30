using System.Collections.Generic;

namespace Khylo.Providers.Books.OpenLibrary;

/// <summary>
/// A book (a "work") on Open Library.
/// </summary>
public class OpenLibraryWork
{
    /// <summary>Gets or sets the work id, e.g. OL450063W.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the title.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the description.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the genres, in everyday words.</summary>
    public IReadOnlyList<string> Genres { get; set; } = [];

    /// <summary>Gets or sets the cover ids.</summary>
    public IReadOnlyList<long> Covers { get; set; } = [];
}
