using System;
using System.Globalization;
using System.Threading.Tasks;
using Khylo.Controller.Events;
using Khylo.Controller.Events.Updates;
using Khylo.Database.Implementations.Entities;
using Khylo.Model.Activity;
using Khylo.Model.Globalization;
using Khylo.Model.Notifications;

namespace Khylo.Server.Implementations.Events.Consumers.Updates
{
    /// <summary>
    /// Creates an entry in the activity log when a plugin is installed.
    /// </summary>
    public class PluginInstalledLogger : IEventConsumer<PluginInstalledEventArgs>
    {
        private readonly ILocalizationManager _localizationManager;
        private readonly IActivityManager _activityManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="PluginInstalledLogger"/> class.
        /// </summary>
        /// <param name="localizationManager">The localization manager.</param>
        /// <param name="activityManager">The activity manager.</param>
        public PluginInstalledLogger(ILocalizationManager localizationManager, IActivityManager activityManager)
        {
            _localizationManager = localizationManager;
            _activityManager = activityManager;
        }

        /// <inheritdoc />
        public async Task OnEvent(PluginInstalledEventArgs eventArgs)
        {
            await _activityManager.CreateAsync(new ActivityLog(
                string.Format(
                    CultureInfo.InvariantCulture,
                    _localizationManager.GetServerLocalizedString("PluginInstalledWithName"),
                    eventArgs.Argument.Name),
                NotificationType.PluginInstalled.ToString(),
                Guid.Empty)
            {
                ShortOverview = string.Format(
                    CultureInfo.InvariantCulture,
                    _localizationManager.GetServerLocalizedString("VersionNumber"),
                    eventArgs.Argument.Version)
            }).ConfigureAwait(false);
        }
    }
}
