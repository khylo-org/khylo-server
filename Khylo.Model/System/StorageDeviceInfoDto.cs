namespace Khylo.Model.System;

/// <summary>
/// Represents storage device metrics and capacity for a mounted filesystem.
/// </summary>
public class StorageDeviceInfoDto
{
    /// <summary>
    /// Gets or sets the device/drive name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the root mount path.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the filesystem type (e.g. NTFS, ext4, APFS, ZFS).
    /// </summary>
    public string FileSystem { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets total volume capacity in bytes.
    /// </summary>
    public long TotalSpace { get; set; }

    /// <summary>
    /// Gets or sets available free space in bytes.
    /// </summary>
    public long FreeSpace { get; set; }

    /// <summary>
    /// Gets or sets used space in bytes.
    /// </summary>
    public long UsedSpace { get; set; }

    /// <summary>
    /// Gets or sets percentage of volume utilized (0 to 100).
    /// </summary>
    public double PercentUsed { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether free space is below the low warning threshold (under 15%).
    /// </summary>
    public bool IsLowSpaceWarning { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether free space is below the critical threshold (under 5%).
    /// </summary>
    public bool IsCriticalSpaceWarning { get; set; }
}
