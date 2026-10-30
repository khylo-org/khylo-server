namespace Khylo.Api.Models.KhyloSetupDtos;

/// <summary>
/// A kind of graphics hardware for converting video.
/// </summary>
public class HardwareOption
{
    /// <summary>Gets or sets the type: nvenc, qsv, amf, vaapi, videotoolbox or none.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Gets or sets a friendly name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether it works on this computer.</summary>
    public bool Works { get; set; }
}
