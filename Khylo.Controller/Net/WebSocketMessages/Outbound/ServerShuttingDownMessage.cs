using System.ComponentModel;
using Khylo.Model.Session;

namespace Khylo.Controller.Net.WebSocketMessages.Outbound;

/// <summary>
/// Server shutting down message.
/// </summary>
public class ServerShuttingDownMessage : OutboundWebSocketMessage
{
    /// <inheritdoc />
    [DefaultValue(SessionMessageType.ServerShuttingDown)]
    public override SessionMessageType MessageType => SessionMessageType.ServerShuttingDown;
}
