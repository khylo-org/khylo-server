using System.Threading;
using System.Threading.Tasks;
using Khylo.Model.Entities;

namespace Khylo.Controller.Library;

/// <summary>
/// Service that classifies disc image files (ISO, IMG, etc.) into their correct media domain.
/// Classification considers both filesystem inspection and library context.
/// </summary>
public interface IDiscImageClassifier
{
    /// <summary>
    /// Classifies a disc image file by inspecting its internal filesystem structure.
    /// </summary>
    /// <param name="path">Absolute path to the disc image file.</param>
    /// <param name="libraryCollectionType">The collection type of the library containing the file, used as context hint.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A classification result for the disc image.</returns>
    Task<IsoClassification> ClassifyAsync(
        string path,
        CollectionTypeOptions? libraryCollectionType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns whether this classifier can handle the given file extension.
    /// </summary>
    /// <param name="extension">The file extension, including the leading dot (e.g. ".iso").</param>
    /// <returns><c>true</c> if this classifier can classify files with the extension; otherwise <c>false</c>.</returns>
    bool CanClassify(string extension);
}
