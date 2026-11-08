namespace Khylo.Api.Controllers;

/// <summary>
/// The Khylo TV channel a device is watching.
/// </summary>
public class MirrorChannelRequest
{
    /// <summary>Gets or sets the channel. Leave out when the device has left the channel.</summary>
    public string? ChannelId { get; set; }
}
