using System.Threading;
using System.Threading.Tasks;
using Khylo.Controller.Events;
using Khylo.Controller.Events.Updates;
using Khylo.Controller.Session;
using Khylo.Model.Session;

namespace Khylo.Server.Implementations.Events.Consumers.Updates
{
    /// <summary>
    /// Notifies admin users when a plugin is installed.
    /// </summary>
    public class PluginInstalledNotifier : IEventConsumer<PluginInstalledEventArgs>
    {
        private readonly ISessionManager _sessionManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="PluginInstalledNotifier"/> class.
        /// </summary>
        /// <param name="sessionManager">The session manager.</param>
        public PluginInstalledNotifier(ISessionManager sessionManager)
        {
            _sessionManager = sessionManager;
        }

        /// <inheritdoc />
        public async Task OnEvent(PluginInstalledEventArgs eventArgs)
        {
            await _sessionManager.SendMessageToAdminSessions(SessionMessageType.PackageInstallationCompleted, eventArgs.Argument, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
