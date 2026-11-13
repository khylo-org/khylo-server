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

    /// <summary>Gets or sets what Windows or the file server said the last time Khylo tried to connect it, when it can't be reached.</summary>
    public string? Reason { get; set; }

    /// <summary>Gets advice about how the folder is written (a dot after the server's name can stop working after a restart), or null.</summary>
    public string? Note => NetworkNames.Warning(Path);
}
