namespace Khylo.Api.Models.EnvironmentDtos;

/// <summary>
/// A network drive someone has mapped on this computer (for example X: pointing to \\nas\movies).
/// </summary>
public class MappedDriveDto
{
    /// <summary>
    /// Gets or sets the drive letter, for example "X:".
    /// </summary>
    public string Letter { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the share's address, for example \\nas\movies.
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the login the drive was mapped with, when it isn't the person's own.
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// Gets or sets the Windows account that mapped the drive.
    /// </summary>
    public string? Account { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this server can open the share right now without a login.
    /// </summary>
    public bool Reachable { get; set; }
}
