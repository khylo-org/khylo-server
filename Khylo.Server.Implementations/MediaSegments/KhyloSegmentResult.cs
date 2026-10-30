namespace Khylo.Server.Implementations.MediaSegments;

/// <summary>
/// What <see cref="KhyloSegmentDetector"/> found in one file, in seconds. Missing values mean
/// nothing was found (a missing button is better than one that skips the wrong part).
/// </summary>
public class KhyloSegmentResult
{
    /// <summary>
    /// Gets or sets the analysis version that produced this result.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Gets or sets the version of the end-credits check this result had, so credits can be
    /// re-checked on their own when that check improves.
    /// </summary>
    public int CreditsVersion { get; set; }

    /// <summary>
    /// Gets or sets where the intro starts.
    /// </summary>
    public double? IntroStart { get; set; }

    /// <summary>
    /// Gets or sets where the intro ends.
    /// </summary>
    public double? IntroEnd { get; set; }

    /// <summary>
    /// Gets or sets where the end credits start.
    /// </summary>
    public double? OutroStart { get; set; }

    /// <summary>
    /// Gets or sets where the end credits end.
    /// </summary>
    public double? OutroEnd { get; set; }
}
