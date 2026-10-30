using System.ComponentModel;
using Khylo.Model.Session;

namespace Khylo.Controller.Net.WebSocketMessages.Inbound;

/// <summary>
/// Keep alive websocket messages.
/// </summary>
public class InboundKeepAliveMessage : InboundWebSocketMessage
{
    /// <inheritdoc />
    [DefaultValue(SessionMessageType.KeepAlive)]
    public override SessionMessageType MessageType => SessionMessageType.KeepAlive;
}
