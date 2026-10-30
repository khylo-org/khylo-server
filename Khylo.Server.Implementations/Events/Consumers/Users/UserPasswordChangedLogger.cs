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
    /// Creates an entry in the activity log when a user's password is changed.
    /// </summary>
    public class UserPasswordChangedLogger : IEventConsumer<UserPasswordChangedEventArgs>
    {
        private readonly ILocalizationManager _localizationManager;
        private readonly IActivityManager _activityManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="UserPasswordChangedLogger"/> class.
        /// </summary>
        /// <param name="localizationManager">The localization manager.</param>
        /// <param name="activityManager">The activity manager.</param>
        public UserPasswordChangedLogger(ILocalizationManager localizationManager, IActivityManager activityManager)
        {
            _localizationManager = localizationManager;
            _activityManager = activityManager;
        }

        /// <inheritdoc />
        public async Task OnEvent(UserPasswordChangedEventArgs eventArgs)
        {
            await _activityManager.CreateAsync(new ActivityLog(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        _localizationManager.GetServerLocalizedString("UserPasswordChangedWithName"),
                        eventArgs.Argument.Username),
                    "UserPasswordChanged",
                    eventArgs.Argument.Id))
                .ConfigureAwait(false);
        }
    }
}
