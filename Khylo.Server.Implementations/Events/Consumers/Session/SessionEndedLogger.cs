using System.Globalization;
using System.Threading.Tasks;
using Khylo.Controller.Events;
using Khylo.Controller.Events.Session;
using Khylo.Database.Implementations.Entities;
using Khylo.Model.Activity;
using Khylo.Model.Globalization;

namespace Khylo.Server.Implementations.Events.Consumers.Session
{
    /// <summary>
    /// Creates an entry in the activity log whenever a session ends.
    /// </summary>
    public class SessionEndedLogger : IEventConsumer<SessionEndedEventArgs>
    {
        private readonly ILocalizationManager _localizationManager;
        private readonly IActivityManager _activityManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="SessionEndedLogger"/> class.
        /// </summary>
        /// <param name="localizationManager">The localization manager.</param>
        /// <param name="activityManager">The activity manager.</param>
        public SessionEndedLogger(ILocalizationManager localizationManager, IActivityManager activityManager)
        {
            _localizationManager = localizationManager;
            _activityManager = activityManager;
        }

        /// <inheritdoc />
        public async Task OnEvent(SessionEndedEventArgs eventArgs)
        {
            if (string.IsNullOrEmpty(eventArgs.Argument.UserName))
            {
                return;
            }

            await _activityManager.CreateAsync(new ActivityLog(
                string.Format(
                    CultureInfo.InvariantCulture,
                    _localizationManager.GetServerLocalizedString("UserOfflineFromDevice"),
                    eventArgs.Argument.UserName,
                    eventArgs.Argument.DeviceName),
                "SessionEnded",
                eventArgs.Argument.UserId)
            {
                ShortOverview = string.Format(
                    CultureInfo.InvariantCulture,
                    _localizationManager.GetServerLocalizedString("LabelIpAddressValue"),
                    eventArgs.Argument.RemoteEndPoint),
            }).ConfigureAwait(false);
        }
    }
}
