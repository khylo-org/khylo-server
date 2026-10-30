using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Controller.Library;
using Khylo.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Implementations.Library;

/// <summary>
/// Classifies disc image files by inspecting structural signatures.
/// </summary>
/// <remarks>
/// Classification priority:
/// 1. Directory structure / system file signatures in the image header (VIDEO_TS = DVD, BDMV = Blu-ray, etc.).
/// 2. Library context as a low-confidence hint.
/// 3. Unknown / NeedsReview if confidence is insufficient.
/// </remarks>
public sealed class DiscImageClassifier : IDiscImageClassifier
{
    private const int HeaderInspectionLength = 65536;

    private static readonly string[] _supportedExtensions = [".iso", ".img", ".bin", ".mdf", ".nrg"];

    private readonly ILogger<DiscImageClassifier> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscImageClassifier"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public DiscImageClassifier(ILogger<DiscImageClassifier> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool CanClassify(string extension)
        => Array.Exists(_supportedExtensions, e => e.Equals(extension, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public async Task<IsoClassification> ClassifyAsync(
        string path,
        CollectionTypeOptions? libraryCollectionType,
        CancellationToken cancellationToken)
    {
        var result = new IsoClassification
        {
            ClassifiedAt = DateTime.UtcNow,
            ClassificationSource = IsoClassificationSource.Automatic,
            NeedsReview = false,
            Confidence = 0.0
        };

        if (!File.Exists(path))
        {
            result.ImageType = IsoType.Unknown;
            result.NeedsReview = true;
            result.Confidence = 0.0;
            return result;
        }

        try
        {
            var headerBytes = await ReadHeaderBytesAsync(path, HeaderInspectionLength, cancellationToken).ConfigureAwait(false);
            var structureResult = InspectDiscStructure(headerBytes);

            if (structureResult.HasValue)
            {
                result.ImageType = structureResult.Value.IsoType;
                result.DetectedFilesystem = structureResult.Value.Filesystem;
                result.DetectedPlatform = structureResult.Value.Platform;
                result.MediaDomain = structureResult.Value.Domain;
                result.Confidence = structureResult.Value.Confidence;
                _logger.LogInformation(
                    "[DiscClassifier] {Path} classified as {Type} with confidence {Confidence:P0} via structure inspection",
                    path,
                    result.ImageType,
                    result.Confidence);
            }
            else
            {
                ApplyLibraryContextHint(result, libraryCollectionType);
            }

            if (result.Confidence < 0.5)
            {
                result.NeedsReview = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DiscClassifier] Failed to classify {Path}, marking as Unknown/NeedsReview", path);
            result.ImageType = IsoType.Unknown;
            result.NeedsReview = true;
            result.Confidence = 0.0;
        }

        return result;
    }

    private static async Task<byte[]> ReadHeaderBytesAsync(string path, int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        await using (fs.ConfigureAwait(false))
        {
            int read = await fs.ReadAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            return read < count ? buffer[..read] : buffer;
        }
    }

    private static (IsoType IsoType, string? Filesystem, string? Platform, string? Domain, double Confidence)? InspectDiscStructure(byte[] headerBytes)
    {
        // Mount-free inspection: well-known directory names and system file signatures in the first 64KB
        var headerText = Encoding.ASCII.GetString(headerBytes);

        if (headerText.Contains("VIDEO_TS", StringComparison.OrdinalIgnoreCase))
        {
            return (IsoType.Dvd, "UDF/ISO9660", null, "movies", 0.92);
        }

        if (headerText.Contains("BDMV", StringComparison.OrdinalIgnoreCase))
        {
            return (IsoType.BluRay, "UDF", null, "movies", 0.92);
        }

        if (headerText.Contains("AUTORUN.INF", StringComparison.OrdinalIgnoreCase)
            || headerText.Contains("SETUP.EXE", StringComparison.OrdinalIgnoreCase)
            || headerText.Contains("INSTALL.EXE", StringComparison.OrdinalIgnoreCase))
        {
            return (IsoType.PcSoftware, "ISO9660", null, null, 0.75);
        }

        return null;
    }

    private static void ApplyLibraryContextHint(IsoClassification result, CollectionTypeOptions? libraryCollectionType)
    {
        if (libraryCollectionType is null)
        {
            result.ImageType = IsoType.Unknown;
            result.NeedsReview = true;
            result.Confidence = 0.0;
            result.ClassificationSource = IsoClassificationSource.Unclassified;
            return;
        }

        result.ClassificationSource = IsoClassificationSource.LibraryContext;

        switch (libraryCollectionType)
        {
            case CollectionTypeOptions.movies:
                result.ImageType = IsoType.Dvd;
                result.MediaDomain = "movies";
                result.Confidence = 0.40; // low confidence: context only, mark for review
                result.NeedsReview = true;
                break;
            default:
                result.ImageType = IsoType.Unknown;
                result.NeedsReview = true;
                result.Confidence = 0.0;
                break;
        }
    }
}
