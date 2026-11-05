using System;

namespace Khylo.Controller.Listening;

/// <summary>
/// A play the app made with no connection and reports later.
/// </summary>
public class OfflinePlay
{
    /// <summary>
    /// Gets or sets the item that was played.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets when it started.
    /// </summary>
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// Gets or sets the seconds actually heard.
    /// </summary>
    public double SecondsListened { get; set; }
}
