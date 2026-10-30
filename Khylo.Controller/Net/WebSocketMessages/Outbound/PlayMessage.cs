using System.ComponentModel;
using Khylo.Model.Session;

namespace Khylo.Controller.Net.WebSocketMessages.Outbound;

/// <summary>
/// Play command websocket message.
/// </summary>
public class PlayMessage : OutboundWebSocketMessage<PlayRequest>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PlayMessage"/> class.
    /// </summary>
    /// <param name="data">The play request.</param>
    public PlayMessage(PlayRequest data)
        : base(data)
    {
    }

    /// <inheritdoc />
    [DefaultValue(SessionMessageType.Play)]
    public override SessionMessageType MessageType => SessionMessageType.Play;
}
