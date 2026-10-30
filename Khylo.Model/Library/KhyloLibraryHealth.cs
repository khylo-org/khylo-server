namespace Khylo.Model.Library;

/// <summary>Overall health state of a library.</summary>
public enum KhyloLibraryHealth
{
    /// <summary>Library is healthy — all paths accessible, no errors.</summary>
    Healthy = 0,

    /// <summary>Library has warnings — some issues but still functional.</summary>
    Degraded = 1,

    /// <summary>Library has critical errors and may not be functional.</summary>
    Failed = 2,

    /// <summary>Library is disabled.</summary>
    Disabled = 3,
}
