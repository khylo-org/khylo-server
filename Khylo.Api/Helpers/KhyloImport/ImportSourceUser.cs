namespace Khylo.Api.Helpers.KhyloImport;

/// <summary>
/// A person on the other server.
/// </summary>
public class ImportSourceUser
{
    /// <summary>Gets or sets the user's id on the other server.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the user's name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the user needs a PIN to switch to (Plex Home).</summary>
    public bool NeedsPin { get; set; }
}
