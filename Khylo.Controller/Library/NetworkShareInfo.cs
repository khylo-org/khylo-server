using System;

namespace Khylo.Controller.Library;

/// <summary>
/// A connected (or remembered) network share.
/// </summary>
public class NetworkShareInfo
{
    /// <summary>Gets or sets the id.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the name shown in Khylo.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the kind of share: "smb" or "nfs".</summary>
    public string Kind { get; set; } = "smb";

    /// <summary>Gets or sets where the share lives, for example \\nas\Movies or nas:/volume1/Movies.</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>Gets or sets the user name used for SMB shares.</summary>
    public string? Username { get; set; }

    /// <summary>Gets or sets a value indicating whether Khylo only reads from the share.</summary>
    public bool ReadOnly { get; set; } = true;

    /// <summary>Gets or sets the folder libraries use for this share.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the share is connected right now.</summary>
    public bool Connected { get; set; }
}
