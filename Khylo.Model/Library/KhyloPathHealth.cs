namespace Khylo.Model.Library;

/// <summary>Health status of a single filesystem path in a library.</summary>
public class KhyloPathHealth
{
    /// <summary>Gets or sets the filesystem path.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the path is currently accessible.</summary>
    public bool IsAccessible { get; set; }

    /// <summary>Gets or sets the reason the path is not accessible (if applicable).</summary>
    public string? InaccessibleReason { get; set; }
}
