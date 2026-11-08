using System.Collections.Generic;

namespace Khylo.Api.Controllers;

/// <summary>
/// Which device to follow and which devices follow it.
/// </summary>
public class MirrorRequest
{
    /// <summary>Gets or sets the device to follow. Leave out to mean the device asking.</summary>
    public string? SourceSessionId { get; set; }

    /// <summary>Gets or sets the devices that follow it.</summary>
    public IReadOnlyList<string> TargetSessionIds { get; set; } = [];
}
