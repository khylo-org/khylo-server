using System;

namespace Khylo.Api.Models;

/// <summary>
/// How a backup is going.
/// </summary>
public class BackupRunStatus
{
    /// <summary>Gets or sets a value indicating whether a backup is running now.</summary>
    public bool InProgress { get; set; }

    /// <summary>Gets or sets when the running backup started.</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>Gets or sets how big the backup file is so far.</summary>
    public long BytesSoFar { get; set; }

    /// <summary>Gets or sets why the last backup didn't finish, if it didn't.</summary>
    public string? Error { get; set; }
}
