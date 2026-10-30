using System;
using System.Collections.Generic;
using Khylo.Model.Entities;

namespace Khylo.Model.Library;

/// <summary>
/// Full administrative representation of a Khylo media library.
/// </summary>
public class KhyloLibraryInfo
{
    /// <summary>Gets or sets the library unique identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the media domain/collection type.</summary>
    public CollectionTypeOptions? MediaDomain { get; set; }

    /// <summary>Gets or sets the filesystem root paths.</summary>
    public IReadOnlyList<string> Locations { get; set; } = [];

    /// <summary>Gets or sets a value indicating whether the library is enabled.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Gets or sets the current scan state.</summary>
    public KhyloLibraryScanState ScanState { get; set; }

    /// <summary>Gets or sets the UTC timestamp of the last completed scan.</summary>
    public DateTime? LastScan { get; set; }

    /// <summary>Gets or sets the UTC timestamp of the current scan start (null if not scanning).</summary>
    public DateTime? CurrentScanStarted { get; set; }

    /// <summary>Gets or sets the library health state.</summary>
    public KhyloLibraryHealth Health { get; set; }

    /// <summary>Gets or sets the last error message if Health is Degraded or Failed.</summary>
    public string? LastError { get; set; }

    /// <summary>Gets or sets the total item count.</summary>
    public long ItemCount { get; set; }

    /// <summary>Gets or sets the count of items that failed processing.</summary>
    public long FailedItemCount { get; set; }

    /// <summary>Gets or sets the count of items missing metadata.</summary>
    public long MissingMetadataCount { get; set; }

    /// <summary>Gets or sets path health — which root locations are accessible.</summary>
    public IReadOnlyList<KhyloPathHealth> PathHealth { get; set; } = [];

    /// <summary>Gets or sets domain-specific configuration properties (varies by media domain).</summary>
    public Dictionary<string, object?> DomainConfig { get; set; } = [];
}
