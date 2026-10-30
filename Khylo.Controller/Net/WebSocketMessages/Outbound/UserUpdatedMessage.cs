using System.ComponentModel;
using Khylo.Model.Dto;
using Khylo.Model.Session;

namespace Khylo.Controller.Net.WebSocketMessages.Outbound;

/// <summary>
/// User updated message.
/// </summary>
public class UserUpdatedMessage : OutboundWebSocketMessage<UserDto>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UserUpdatedMessage"/> class.
    /// </summary>
    /// <param name="data">The user dto.</param>
    public UserUpdatedMessage(UserDto data)
        : base(data)
    {
    }

    /// <inheritdoc />
    [DefaultValue(SessionMessageType.UserUpdated)]
    public override SessionMessageType MessageType => SessionMessageType.UserUpdated;
}
