using System.ComponentModel;
using Khylo.Model.Session;

namespace Khylo.Controller.Net.WebSocketMessages.Inbound;

/// <summary>
/// Sessions stop message.
/// </summary>
public class SessionsStopMessage : InboundWebSocketMessage
{
    /// <inheritdoc />
    [DefaultValue(SessionMessageType.SessionsStop)]
    public override SessionMessageType MessageType => SessionMessageType.SessionsStop;
}
