using System;

namespace Khylo.Api.Helpers.KhyloImport;

/// <summary>
/// Which person on the other server goes to which Khylo user.
/// </summary>
public class ImportUserMapping
{
    /// <summary>Gets or sets the user's id on the other server.</summary>
    public string SourceUserId { get; set; } = string.Empty;

    /// <summary>Gets or sets the user's name on the other server.</summary>
    public string SourceUserName { get; set; } = string.Empty;

    /// <summary>Gets or sets the Khylo user (or home profile) it goes to.</summary>
    public Guid KhyloUserId { get; set; }

    /// <summary>Gets or sets the Plex Home PIN, for protected Plex users.</summary>
    public string? Pin { get; set; }
}
