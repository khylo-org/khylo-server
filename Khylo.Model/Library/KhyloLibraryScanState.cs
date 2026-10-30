namespace Khylo.Model.Library;

/// <summary>Scan state of a library.</summary>
public enum KhyloLibraryScanState
{
    /// <summary>Library is idle — no active scan.</summary>
    Idle = 0,

    /// <summary>A scan is currently running.</summary>
    Scanning = 1,

    /// <summary>Scan completed with errors.</summary>
    CompletedWithErrors = 2,

    /// <summary>Library has never been scanned.</summary>
    NeverScanned = 3,
}
