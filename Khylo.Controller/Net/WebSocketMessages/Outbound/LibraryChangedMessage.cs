using System.ComponentModel;
using Khylo.Model.Entities;
using Khylo.Model.Session;

namespace Khylo.Controller.Net.WebSocketMessages.Outbound;

/// <summary>
/// Library changed message.
/// </summary>
public class LibraryChangedMessage : OutboundWebSocketMessage<LibraryUpdateInfo>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryChangedMessage"/> class.
    /// </summary>
    /// <param name="data">The library update info.</param>
    public LibraryChangedMessage(LibraryUpdateInfo data)
        : base(data)
    {
    }

    /// <inheritdoc />
    [DefaultValue(SessionMessageType.LibraryChanged)]
    public override SessionMessageType MessageType => SessionMessageType.LibraryChanged;
}
