using System;

namespace Khylo.Controller.Library;

/// <summary>
/// One library location and whether it can be reached.
/// </summary>
public class StorageLocationStatus
{
    /// <summary>Gets or sets the library's name.</summary>
    public string Library { get; set; } = string.Empty;

    /// <summary>Gets or sets the folder.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether it could be reached when last checked.</summary>
    public bool Reachable { get; set; }

    /// <summary>Gets or sets when it last went down or came back (UTC).</summary>
    public DateTime Since { get; set; }

    /// <summary>Gets or sets when it was last checked (UTC).</summary>
    public DateTime LastChecked { get; set; }
}
