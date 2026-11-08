namespace Khylo.Controller.Mirroring;

/// <summary>
/// One device that is following another in a mirror.
/// </summary>
public class MirrorTargetInfo
{
    /// <summary>Gets or sets the device's session.</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Gets or sets the device's name (for example "Bedroom TV").</summary>
    public string? DeviceName { get; set; }

    /// <summary>Gets or sets the app on the device.</summary>
    public string? Client { get; set; }
}
