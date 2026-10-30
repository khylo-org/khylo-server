#pragma warning disable CA1002, CA2227 // Lists are filled in and sent as JSON

using System;
using System.Collections.Generic;

namespace Khylo.Api.Helpers.KhyloImport;

/// <summary>
/// Where an import (or preview) is.
/// </summary>
public class ImportStatus
{
    /// <summary>Gets or sets the state: Idle, Running, Done or Failed.</summary>
    public string State { get; set; } = "Idle";

    /// <summary>Gets or sets a value indicating whether this was a preview.</summary>
    public bool DryRun { get; set; }

    /// <summary>Gets or sets what it's doing now.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Gets or sets how far along it is, 0 to 100.</summary>
    public double Percent { get; set; }

    /// <summary>Gets or sets the results so far, one per person.</summary>
    public List<ImportUserResult> Users { get; set; } = [];

    /// <summary>Gets or sets when it started.</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>Gets or sets when it finished.</summary>
    public DateTime? FinishedAt { get; set; }
}
