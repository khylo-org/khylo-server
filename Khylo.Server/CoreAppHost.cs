using System;
using System.Collections.Generic;
using System.Reflection;
using Khylo.Api.WebSocketListeners;
using Khylo.Controller;
using Khylo.Controller.Authentication;
using Khylo.Controller.BaseItemManager;
using Khylo.Controller.Devices;
using Khylo.Controller.Drawing;
using Khylo.Controller.Events;
using Khylo.Controller.Library;
using Khylo.Controller.Lyrics;
using Khylo.Controller.Net;
using Khylo.Controller.Security;
using Khylo.Controller.Trickplay;
using Khylo.Database.Implementations;
using Khylo.Drawing;
using Khylo.Drawing.Skia;
using Khylo.LiveTv;
using Khylo.Model.Activity;
using Khylo.Providers.Lyric;
using Khylo.Server.Core;
using Khylo.Server.Core.Session;
using Khylo.Server.Implementations.Activity;
using Khylo.Server.Implementations.Devices;
using Khylo.Server.Implementations.Events;
using Khylo.Server.Implementations.Extensions;
using Khylo.Server.Implementations.Security;
using Khylo.Server.Implementations.Trickplay;
using Khylo.Server.Implementations.Users;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Khylo.Server
{
    /// <summary>
    /// Implementation of the abstract <see cref="ApplicationHost" /> class.
    /// </summary>
    public class CoreAppHost : ApplicationHost
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CoreAppHost" /> class.
        /// </summary>
        /// <param name="applicationPaths">The <see cref="ServerApplicationPaths" /> to be used by the <see cref="CoreAppHost" />.</param>
        /// <param name="loggerFactory">The <see cref="ILoggerFactory" /> to be used by the <see cref="CoreAppHost" />.</param>
        /// <param name="options">The <see cref="StartupOptions" /> to be used by the <see cref="CoreAppHost" />.</param>
        /// <param name="startupConfig">The <see cref="IConfiguration" /> to be used by the <see cref="CoreAppHost" />.</param>
        public CoreAppHost(
            IServerApplicationPaths applicationPaths,
            ILoggerFactory loggerFactory,
            IStartupOptions options,
            IConfiguration startupConfig)
            : base(
                applicationPaths,
                loggerFactory,
                options,
                startupConfig)
        {
        }

        /// <inheritdoc/>
        protected override void RegisterServices(IServiceCollection serviceCollection)
        {
            // Register an image encoder
            bool useSkiaEncoder = SkiaEncoder.IsNativeLibAvailable();
            Type imageEncoderType = useSkiaEncoder
                ? typeof(SkiaEncoder)
                : typeof(NullImageEncoder);
            serviceCollection.AddSingleton(typeof(IImageEncoder), imageEncoderType);

            // Log a warning if the Skia encoder could not be used
            if (!useSkiaEncoder)
            {
                Logger.LogWarning("Skia not available. Will fallback to {ImageEncoder}.", nameof(NullImageEncoder));
            }

            serviceCollection.AddEventServices();
            serviceCollection.AddSingleton<IBaseItemManager, BaseItemManager>();
            serviceCollection.AddSingleton<IEventManager, EventManager>();

            serviceCollection.AddSingleton<IActivityManager, ActivityManager>();
            serviceCollection.AddSingleton<IUserManager, UserManager>();
            serviceCollection.AddSingleton<IAuthenticationProvider, DefaultAuthenticationProvider>();
            serviceCollection.AddSingleton<IAuthenticationProvider, InvalidAuthProvider>();
            serviceCollection.AddSingleton<IPasswordResetProvider, DefaultPasswordResetProvider>();
            serviceCollection.AddSingleton<IDisplayPreferencesManager, DisplayPreferencesManager>();
            serviceCollection.AddSingleton<IDeviceManager, DeviceManager>();
            serviceCollection.AddSingleton<ITrickplayManager, TrickplayManager>();

            // TODO search the assemblies instead of adding them manually?
            serviceCollection.AddSingleton<IWebSocketListener, SessionWebSocketListener>();
            serviceCollection.AddSingleton<IWebSocketListener, ActivityLogWebSocketListener>();
            serviceCollection.AddSingleton<IWebSocketListener, ScheduledTasksWebSocketListener>();
            serviceCollection.AddSingleton<IWebSocketListener, SessionInfoWebSocketListener>();

            serviceCollection.AddSingleton<IAuthorizationContext, AuthorizationContext>();

            serviceCollection.AddSingleton<IDiscImageClassifier, Khylo.Server.Implementations.Library.DiscImageClassifier>();

            serviceCollection.AddScoped<IAuthenticationManager, AuthenticationManager>();

            foreach (var type in GetExportTypes<ILyricProvider>())
            {
                serviceCollection.AddSingleton(typeof(ILyricProvider), type);
            }

            foreach (var type in GetExportTypes<ILyricParser>())
            {
                serviceCollection.AddSingleton(typeof(ILyricParser), type);
            }

            base.RegisterServices(serviceCollection);
        }

        /// <inheritdoc />
        protected override IEnumerable<Assembly> GetAssembliesWithPartsInternal()
        {
            // Khylo.Server
            yield return typeof(CoreAppHost).Assembly;

            // Khylo.Database.Implementations
            yield return typeof(KhyloDbContext).Assembly;

            // Khylo.Server.Implementations
            yield return typeof(ServiceCollectionExtensions).Assembly;

            // Khylo.LiveTv
            yield return typeof(LiveTvManager).Assembly;
        }
    }
}
