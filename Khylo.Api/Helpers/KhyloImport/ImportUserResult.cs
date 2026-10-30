#pragma warning disable CA1002, CA2227 // Lists are filled in and sent as JSON

using System.Collections.Generic;

namespace Khylo.Api.Helpers.KhyloImport;

/// <summary>
/// What happened for one person.
/// </summary>
public class ImportUserResult
{
    /// <summary>Gets or sets the name on the other server.</summary>
    public string SourceUserName { get; set; } = string.Empty;

    /// <summary>Gets or sets the Khylo user's name.</summary>
    public string KhyloUserName { get; set; } = string.Empty;

    /// <summary>Gets or sets how many things had history on the other server.</summary>
    public int Found { get; set; }

    /// <summary>Gets or sets how many of them were found in Khylo.</summary>
    public int Matched { get; set; }

    /// <summary>Gets or sets how many were watched (or listened to).</summary>
    public int Watched { get; set; }

    /// <summary>Gets or sets how many were part way through.</summary>
    public int InProgress { get; set; }

    /// <summary>Gets or sets how many plays were carried over.</summary>
    public int Plays { get; set; }

    /// <summary>Gets or sets how many favorites were carried over.</summary>
    public int Favorites { get; set; }

    /// <summary>Gets or sets how many already had newer progress in Khylo and were kept as they were.</summary>
    public int KeptNewer { get; set; }

    /// <summary>Gets or sets how many couldn't be found in Khylo.</summary>
    public int Unmatched { get; set; }

    /// <summary>Gets or sets some of the titles that couldn't be found.</summary>
    public List<string> UnmatchedExamples { get; set; } = [];

    /// <summary>Gets or sets a problem with this person, if any.</summary>
    public string? Error { get; set; }
}
