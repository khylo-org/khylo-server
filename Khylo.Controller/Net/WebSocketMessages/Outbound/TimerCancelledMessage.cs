using System.ComponentModel;
using Khylo.Controller.LiveTv;
using Khylo.Model.Session;

namespace Khylo.Controller.Net.WebSocketMessages.Outbound;

/// <summary>
/// Timer cancelled message.
/// </summary>
public class TimerCancelledMessage : OutboundWebSocketMessage<TimerEventInfo>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TimerCancelledMessage"/> class.
    /// </summary>
    /// <param name="data">Timer event info.</param>
    public TimerCancelledMessage(TimerEventInfo data)
        : base(data)
    {
    }

    /// <inheritdoc />
    [DefaultValue(SessionMessageType.TimerCancelled)]
    public override SessionMessageType MessageType => SessionMessageType.TimerCancelled;
}
