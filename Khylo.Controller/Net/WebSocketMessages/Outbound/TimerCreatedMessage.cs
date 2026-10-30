using System.ComponentModel;
using Khylo.Controller.LiveTv;
using Khylo.Model.Session;

namespace Khylo.Controller.Net.WebSocketMessages.Outbound;

/// <summary>
/// Timer created message.
/// </summary>
public class TimerCreatedMessage : OutboundWebSocketMessage<TimerEventInfo>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TimerCreatedMessage"/> class.
    /// </summary>
    /// <param name="data">Timer event info.</param>
    public TimerCreatedMessage(TimerEventInfo data)
        : base(data)
    {
    }

    /// <inheritdoc />
    [DefaultValue(SessionMessageType.TimerCreated)]
    public override SessionMessageType MessageType => SessionMessageType.TimerCreated;
}
