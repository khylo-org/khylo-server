using System.Collections.Generic;

namespace Khylo.Api.Models.KhyloSetupDtos;

/// <summary>
/// What graphics hardware this computer has for converting video.
/// </summary>
public class HardwareDetection
{
    /// <summary>Gets or sets the kinds tried, and whether each works.</summary>
    public IReadOnlyList<HardwareOption> Options { get; set; } = [];

    /// <summary>Gets or sets the kind in use now.</summary>
    public string Current { get; set; } = "none";
}
