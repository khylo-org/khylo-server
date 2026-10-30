using System.Collections.Generic;
using System.ComponentModel;
using Khylo.Model.Activity;
using Khylo.Model.Session;

namespace Khylo.Controller.Net.WebSocketMessages.Outbound;

/// <summary>
/// Activity log created message.
/// </summary>
public class ActivityLogEntryMessage : OutboundWebSocketMessage<IReadOnlyList<ActivityLogEntry>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ActivityLogEntryMessage"/> class.
    /// </summary>
    /// <param name="data">List of activity log entries.</param>
    public ActivityLogEntryMessage(IReadOnlyList<ActivityLogEntry> data)
        : base(data)
    {
    }

    /// <inheritdoc />
    [DefaultValue(SessionMessageType.ActivityLogEntry)]
    public override SessionMessageType MessageType => SessionMessageType.ActivityLogEntry;
}
