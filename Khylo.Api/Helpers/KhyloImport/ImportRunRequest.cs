using System.Collections.Generic;

namespace Khylo.Api.Helpers.KhyloImport;

/// <summary>
/// Starts an import, or a preview of one.
/// </summary>
public class ImportRunRequest : ImportConnectRequest
{
    /// <summary>Gets or sets a value indicating whether to only count what would be imported.</summary>
    public bool DryRun { get; set; }

    /// <summary>Gets or sets who goes where.</summary>
    public IReadOnlyList<ImportUserMapping> Users { get; set; } = [];
}
