using System.Threading.Tasks;
using Khylo.Controller.Events;
using Khylo.Controller.Listening;
using Khylo.Data.Events.Users;

namespace Khylo.Server.Implementations.Events.Consumers.Users
{
    /// <summary>
    /// Deletes a person's listening and watching history when the person is deleted.
    /// </summary>
    public class UserDeletedListeningCleaner : IEventConsumer<UserDeletedEventArgs>
    {
        private readonly IListeningHistory _history;

        /// <summary>
        /// Initializes a new instance of the <see cref="UserDeletedListeningCleaner"/> class.
        /// </summary>
        /// <param name="history">The listening history.</param>
        public UserDeletedListeningCleaner(IListeningHistory history)
        {
            _history = history;
        }

        /// <inheritdoc />
        public Task OnEvent(UserDeletedEventArgs eventArgs)
        {
            _history.DeleteForUser(eventArgs.Argument.Id);
            return Task.CompletedTask;
        }
    }
}
