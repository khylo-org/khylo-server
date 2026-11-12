using System;

namespace Khylo.Controller.Updates;

/// <summary>
/// One line of what an update did, for the dashboard's update log.
/// </summary>
public class KhyloUpdateLogLine
{
    /// <summary>Gets or sets when it happened (UTC).</summary>
    public DateTime At { get; set; }

    /// <summary>Gets or sets what happened, in plain words.</summary>
    public string Text { get; set; } = string.Empty;
}
