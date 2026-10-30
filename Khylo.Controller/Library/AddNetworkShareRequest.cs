namespace Khylo.Controller.Library;

/// <summary>
/// A request to connect a share.
/// </summary>
public class AddNetworkShareRequest
{
    /// <summary>Gets or sets the kind of share: "smb" or "nfs".</summary>
    public string Kind { get; set; } = "smb";

    /// <summary>Gets or sets where the share lives: \\nas\Movies, //nas/Movies, smb://nas/Movies, nas:/volume1/Movies.</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>Gets or sets the user name for SMB shares; empty for guest access.</summary>
    public string? Username { get; set; }

    /// <summary>Gets or sets the password for SMB shares.</summary>
    public string? Password { get; set; }

    /// <summary>Gets or sets a value indicating whether Khylo should only read from the share.</summary>
    public bool ReadOnly { get; set; } = true;

    /// <summary>Gets or sets an optional name; defaults to the share's folder name.</summary>
    public string? Name { get; set; }
}
