using System.ComponentModel;
using Khylo.Model.Session;

namespace Khylo.Controller.Net.WebSocketMessages.Outbound;

/// <summary>
/// Restart required.
/// </summary>
public class RestartRequiredMessage : OutboundWebSocketMessage
{
    /// <inheritdoc />
    [DefaultValue(SessionMessageType.RestartRequired)]
    public override SessionMessageType MessageType => SessionMessageType.RestartRequired;
}
