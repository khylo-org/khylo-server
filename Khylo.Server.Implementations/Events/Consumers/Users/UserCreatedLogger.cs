using System.Globalization;
using System.Threading.Tasks;
using Khylo.Controller.Events;
using Khylo.Data.Events.Users;
using Khylo.Database.Implementations.Entities;
using Khylo.Model.Activity;
using Khylo.Model.Globalization;

namespace Khylo.Server.Implementations.Events.Consumers.Users
{
    /// <summary>
    /// Creates an entry in the activity log when a user is created.
    /// </summary>
    public class UserCreatedLogger : IEventConsumer<UserCreatedEventArgs>
    {
        private readonly ILocalizationManager _localizationManager;
        private readonly IActivityManager _activityManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="UserCreatedLogger"/> class.
        /// </summary>
        /// <param name="localizationManager">The localization manager.</param>
        /// <param name="activityManager">The activity manager.</param>
        public UserCreatedLogger(ILocalizationManager localizationManager, IActivityManager activityManager)
        {
            _localizationManager = localizationManager;
            _activityManager = activityManager;
        }

        /// <inheritdoc />
        public async Task OnEvent(UserCreatedEventArgs eventArgs)
        {
            await _activityManager.CreateAsync(new ActivityLog(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        _localizationManager.GetServerLocalizedString("UserCreatedWithName"),
                        eventArgs.Argument.Username),
                    "UserCreated",
                    eventArgs.Argument.Id))
                .ConfigureAwait(false);
        }
    }
}
