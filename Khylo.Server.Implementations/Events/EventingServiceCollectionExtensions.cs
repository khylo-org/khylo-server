using Khylo.Common.Updates;
using Khylo.Controller.Events;
using Khylo.Controller.Events.Authentication;
using Khylo.Controller.Events.Session;
using Khylo.Controller.Events.Updates;
using Khylo.Controller.Library;
using Khylo.Controller.Lyrics;
using Khylo.Controller.Subtitles;
using Khylo.Data.Events.System;
using Khylo.Data.Events.Users;
using Khylo.Model.Tasks;
using Khylo.Server.Implementations.Events.Consumers.Library;
using Khylo.Server.Implementations.Events.Consumers.Security;
using Khylo.Server.Implementations.Events.Consumers.Session;
using Khylo.Server.Implementations.Events.Consumers.System;
using Khylo.Server.Implementations.Events.Consumers.Updates;
using Khylo.Server.Implementations.Events.Consumers.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Khylo.Server.Implementations.Events
{
    /// <summary>
    /// A class containing extensions to <see cref="IServiceCollection"/> for eventing.
    /// </summary>
    public static class EventingServiceCollectionExtensions
    {
        /// <summary>
        /// Adds the event services to the service collection.
        /// </summary>
        /// <param name="collection">The service collection.</param>
        public static void AddEventServices(this IServiceCollection collection)
        {
            // Library consumers
            collection.AddScoped<IEventConsumer<LyricDownloadFailureEventArgs>, LyricDownloadFailureLogger>();
            collection.AddScoped<IEventConsumer<SubtitleDownloadFailureEventArgs>, SubtitleDownloadFailureLogger>();

            // Security consumers
            collection.AddScoped<IEventConsumer<AuthenticationRequestEventArgs>, AuthenticationFailedLogger>();
            collection.AddScoped<IEventConsumer<AuthenticationResultEventArgs>, AuthenticationSucceededLogger>();

            // Session consumers
            collection.AddScoped<IEventConsumer<PlaybackStartEventArgs>, PlaybackStartLogger>();
            collection.AddScoped<IEventConsumer<PlaybackStopEventArgs>, PlaybackStopLogger>();
            collection.AddScoped<IEventConsumer<SessionEndedEventArgs>, SessionEndedLogger>();
            collection.AddScoped<IEventConsumer<SessionStartedEventArgs>, SessionStartedLogger>();

            // System consumers
            collection.AddScoped<IEventConsumer<PendingRestartEventArgs>, PendingRestartNotifier>();
            collection.AddScoped<IEventConsumer<TaskCompletionEventArgs>, TaskCompletedLogger>();
            collection.AddScoped<IEventConsumer<TaskCompletionEventArgs>, TaskCompletedNotifier>();

            // Update consumers
            collection.AddScoped<IEventConsumer<PluginInstallationCancelledEventArgs>, PluginInstallationCancelledNotifier>();
            collection.AddScoped<IEventConsumer<InstallationFailedEventArgs>, PluginInstallationFailedLogger>();
            collection.AddScoped<IEventConsumer<InstallationFailedEventArgs>, PluginInstallationFailedNotifier>();
            collection.AddScoped<IEventConsumer<PluginInstalledEventArgs>, PluginInstalledLogger>();
            collection.AddScoped<IEventConsumer<PluginInstalledEventArgs>, PluginInstalledNotifier>();
            collection.AddScoped<IEventConsumer<PluginInstallingEventArgs>, PluginInstallingNotifier>();
            collection.AddScoped<IEventConsumer<PluginUninstalledEventArgs>, PluginUninstalledLogger>();
            collection.AddScoped<IEventConsumer<PluginUninstalledEventArgs>, PluginUninstalledNotifier>();
            collection.AddScoped<IEventConsumer<PluginUpdatedEventArgs>, PluginUpdatedLogger>();

            // User consumers
            collection.AddScoped<IEventConsumer<UserCreatedEventArgs>, UserCreatedLogger>();
            collection.AddScoped<IEventConsumer<UserDeletedEventArgs>, UserDeletedLogger>();
            collection.AddScoped<IEventConsumer<UserDeletedEventArgs>, UserDeletedNotifier>();
            collection.AddScoped<IEventConsumer<UserDeletedEventArgs>, UserDeletedListeningCleaner>();
            collection.AddScoped<IEventConsumer<UserLockedOutEventArgs>, UserLockedOutLogger>();
            collection.AddScoped<IEventConsumer<UserPasswordChangedEventArgs>, UserPasswordChangedLogger>();
            collection.AddScoped<IEventConsumer<UserUpdatedEventArgs>, UserUpdatedNotifier>();
        }
    }
}
