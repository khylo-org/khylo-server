using System;

namespace Khylo.Api.Models.CollectionDtos;

/// <summary>
/// A collection the current user can add to, for "Add to collection" pickers.
/// </summary>
public class EditableCollectionDto
{
    /// <summary>
    /// Gets or sets the collection id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the collection name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether everyone on the server sees the collection (otherwise
    /// it is the user's personal collection).
    /// </summary>
    public bool IsShared { get; set; }

    /// <summary>
    /// Gets or sets how many titles are in the collection.
    /// </summary>
    public int ItemCount { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the item asked about is already in the collection.
    /// </summary>
    public bool ContainsItem { get; set; }
}
