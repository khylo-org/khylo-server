using System;

namespace Khylo.Controller.Updates;

/// <summary>
/// A build on the release page.
/// </summary>
public class KhyloUpdateInfo
{
    /// <summary>Gets or sets the build, for example DEV-V26.11.04.02.</summary>
    public string Build { get; set; } = string.Empty;

    /// <summary>Gets or sets when it was published.</summary>
    public DateTime? PublishedAt { get; set; }

    /// <summary>Gets or sets the release notes (what's new).</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>Gets or sets the page for this release.</summary>
    public string Url { get; set; } = string.Empty;
}
