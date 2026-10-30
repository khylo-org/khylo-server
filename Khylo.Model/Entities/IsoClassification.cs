using System;

namespace Khylo.Model.Entities;

/// <summary>
/// Represents the persisted classification result of a disc/ISO image.
/// </summary>
public class IsoClassification
{
    /// <summary>Gets or sets the disc image type (DVD, Blu-ray, PS1, data, etc.).</summary>
    public IsoType ImageType { get; set; } = IsoType.Unknown;

    /// <summary>Gets or sets the disc filesystem detected (UDF, ISO9660, CDFS, etc.).</summary>
    public string? DetectedFilesystem { get; set; }

    /// <summary>Gets or sets the target media domain this image belongs to (video, game, etc.).</summary>
    public string? MediaDomain { get; set; }

    /// <summary>Gets or sets the detected platform (e.g. PlayStation, Saturn).</summary>
    public string? DetectedPlatform { get; set; }

    /// <summary>Gets or sets the disc region code where detectable.</summary>
    public string? Region { get; set; }

    /// <summary>Gets or sets the classification confidence score from 0.0 to 1.0.</summary>
    public double Confidence { get; set; }

    /// <summary>Gets or sets the source of the classification decision.</summary>
    public IsoClassificationSource ClassificationSource { get; set; } = IsoClassificationSource.Automatic;

    /// <summary>Gets or sets the classification timestamp.</summary>
    public DateTime ClassifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets additional notes from administrator manual override.</summary>
    public string? AdminNote { get; set; }

    /// <summary>Gets or sets a value indicating whether this classification needs administrator review.</summary>
    public bool NeedsReview { get; set; }
}
