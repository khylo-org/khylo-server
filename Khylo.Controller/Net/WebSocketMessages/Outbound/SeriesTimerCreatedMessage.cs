using System.ComponentModel;
using Khylo.Controller.LiveTv;
using Khylo.Model.Session;

namespace Khylo.Controller.Net.WebSocketMessages.Outbound;

/// <summary>
/// Series timer created message.
/// </summary>
public class SeriesTimerCreatedMessage : OutboundWebSocketMessage<TimerEventInfo>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SeriesTimerCreatedMessage"/> class.
    /// </summary>
    /// <param name="data">timer event info.</param>
    public SeriesTimerCreatedMessage(TimerEventInfo data)
        : base(data)
    {
    }

    /// <inheritdoc />
    [DefaultValue(SessionMessageType.SeriesTimerCreated)]
    public override SessionMessageType MessageType => SessionMessageType.SeriesTimerCreated;
}
