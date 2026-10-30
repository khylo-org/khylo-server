using System.ComponentModel;
using Khylo.Controller.LiveTv;
using Khylo.Model.Session;

namespace Khylo.Controller.Net.WebSocketMessages.Outbound;

/// <summary>
/// Series timer cancelled message.
/// </summary>
public class SeriesTimerCancelledMessage : OutboundWebSocketMessage<TimerEventInfo>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SeriesTimerCancelledMessage"/> class.
    /// </summary>
    /// <param name="data">The timer event info.</param>
    public SeriesTimerCancelledMessage(TimerEventInfo data)
        : base(data)
    {
    }

    /// <inheritdoc />
    [DefaultValue(SessionMessageType.SeriesTimerCancelled)]
    public override SessionMessageType MessageType => SessionMessageType.SeriesTimerCancelled;
}
