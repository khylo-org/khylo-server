using Khylo.Controller.Entities;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;
using Khylo.Model.Providers;

namespace Khylo.Providers.Books.OpenLibrary;

/// <summary>
/// The Open Library work id of a book or audiobook.
/// </summary>
public class OpenLibraryExternalId : IExternalId
{
    /// <inheritdoc />
    public string ProviderName => OpenLibrarySource.ProviderName;

    /// <inheritdoc />
    public string Key => OpenLibrarySource.IdKey;

    /// <inheritdoc />
    public ExternalIdMediaType? Type => null;

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item) => item is Book or AudioBook;
}
