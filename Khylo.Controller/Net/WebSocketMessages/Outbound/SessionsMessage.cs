using System.Collections.Generic;
using System.ComponentModel;
using Khylo.Controller.Session;
using Khylo.Model.Dto;
using Khylo.Model.Session;

namespace Khylo.Controller.Net.WebSocketMessages.Outbound;

/// <summary>
/// Sessions message.
/// </summary>
public class SessionsMessage : OutboundWebSocketMessage<IReadOnlyList<SessionInfoDto>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SessionsMessage"/> class.
    /// </summary>
    /// <param name="data">Session info.</param>
    public SessionsMessage(IReadOnlyList<SessionInfoDto> data)
        : base(data)
    {
    }

    /// <inheritdoc />
    [DefaultValue(SessionMessageType.Sessions)]
    public override SessionMessageType MessageType => SessionMessageType.Sessions;
}
