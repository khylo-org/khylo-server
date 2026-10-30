using System.ComponentModel;
using Khylo.Model.Session;
using Khylo.Model.Updates;

namespace Khylo.Controller.Net.WebSocketMessages.Outbound;

/// <summary>
/// Plugin installation completed message.
/// </summary>
public class PluginInstallationCompletedMessage : OutboundWebSocketMessage<InstallationInfo>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginInstallationCompletedMessage"/> class.
    /// </summary>
    /// <param name="data">Installation info.</param>
    public PluginInstallationCompletedMessage(InstallationInfo data)
        : base(data)
    {
    }

    /// <inheritdoc />
    [DefaultValue(SessionMessageType.PackageInstallationCompleted)]
    public override SessionMessageType MessageType => SessionMessageType.PackageInstallationCompleted;
}
