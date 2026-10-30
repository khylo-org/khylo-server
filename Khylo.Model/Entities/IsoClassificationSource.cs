namespace Khylo.Model.Entities;

/// <summary>The source of an ISO classification decision.</summary>
public enum IsoClassificationSource
{
    /// <summary>Classified automatically by the disc inspector pipeline.</summary>
    Automatic = 0,

    /// <summary>Inferred from library context (e.g. located in a Games library).</summary>
    LibraryContext = 1,

    /// <summary>Manually assigned by an administrator.</summary>
    ManualOverride = 2,

    /// <summary>Unclassified — awaiting inspection.</summary>
    Unclassified = 99,
}
