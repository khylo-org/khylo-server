using System;
using Khylo.Model.Configuration;

namespace Khylo.Api.Models.LibraryStructureDto;

/// <summary>
/// Update library options dto.
/// </summary>
public class UpdateLibraryOptionsDto
{
    /// <summary>
    /// Gets or sets the library item id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets library options.
    /// </summary>
    public LibraryOptions? LibraryOptions { get; set; }
}
