namespace Khylo.Api.Models.EnvironmentDtos;

/// <summary>
/// A request to give Khylo access to a folder on the server's PC.
/// </summary>
public class GiveFolderAccessDto
{
    /// <summary>
    /// Gets or sets the folder.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Khylo may also add, change and remove files in the folder.
    /// </summary>
    public bool AllowChanges { get; set; }
}
