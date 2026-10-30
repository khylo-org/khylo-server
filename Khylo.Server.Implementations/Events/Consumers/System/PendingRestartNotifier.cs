using System.Threading;
using System.Threading.Tasks;
using Khylo.Controller.Events;
using Khylo.Controller.Session;
using Khylo.Data.Events.System;

namespace Khylo.Server.Implementations.Events.Consumers.System
{
    /// <summary>
    /// Notifies users when there is a pending restart.
    /// </summary>
    public class PendingRestartNotifier : IEventConsumer<PendingRestartEventArgs>
    {
        private readonly ISessionManager _sessionManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="PendingRestartNotifier"/> class.
        /// </summary>
        /// <param name="sessionManager">The session manager.</param>
        public PendingRestartNotifier(ISessionManager sessionManager)
        {
            _sessionManager = sessionManager;
        }

        /// <inheritdoc />
        public async Task OnEvent(PendingRestartEventArgs eventArgs)
        {
            await _sessionManager.SendRestartRequiredNotification(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
