using System.Threading;
using System.Threading.Tasks;
using Khylo.Controller.Events;
using Khylo.Controller.Session;
using Khylo.Model.Session;
using Khylo.Model.Tasks;

namespace Khylo.Server.Implementations.Events.Consumers.System
{
    /// <summary>
    /// Notifies admin users when a task is completed.
    /// </summary>
    public class TaskCompletedNotifier : IEventConsumer<TaskCompletionEventArgs>
    {
        private readonly ISessionManager _sessionManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="TaskCompletedNotifier"/> class.
        /// </summary>
        /// <param name="sessionManager">The session manager.</param>
        public TaskCompletedNotifier(ISessionManager sessionManager)
        {
            _sessionManager = sessionManager;
        }

        /// <inheritdoc />
        public async Task OnEvent(TaskCompletionEventArgs eventArgs)
        {
            await _sessionManager.SendMessageToAdminSessions(SessionMessageType.ScheduledTaskEnded, eventArgs.Result, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
