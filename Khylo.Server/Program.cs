using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using Khylo.Common.Configuration;
using Khylo.Common.Net;
using Khylo.Controller;
using Khylo.Database.Implementations;
using Khylo.Server.Core;
using Khylo.Server.Core.Configuration;
using Khylo.Server.Core.Serialization;
using Khylo.Server.Extensions;
using Khylo.Server.Helpers;
using Khylo.Server.Implementations.DatabaseConfiguration;
using Khylo.Server.Implementations.Extensions;
using Khylo.Server.Implementations.StorageHelpers;
using Khylo.Server.Implementations.SystemBackupService;
using Khylo.Server.Migrations;
using Khylo.Server.Migrations.Stages;
using Khylo.Server.ServerSetupApp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using static Khylo.Controller.Extensions.ConfigurationExtensions;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Khylo.Server
{
    /// <summary>
    /// Class containing the entry point of the application.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// The name of logging configuration file containing application defaults.
        /// </summary>
        public const string LoggingConfigFileDefault = "logging.default.json";

        /// <summary>
        /// The name of the logging configuration file containing the system-specific override settings.
        /// </summary>
        public const string LoggingConfigFileSystem = "logging.json";

        private static readonly SerilogLoggerFactory _loggerFactory = new SerilogLoggerFactory();
        private static SetupServer? _setupServer;
        private static CoreAppHost? _appHost;
        private static IHost? _khyloHost = null;
        private static long _startTimestamp;
        private static ILogger _logger = NullLogger.Instance;
        private static bool _restartOnShutdown;
        private static IStartupLogger<KhyloMigrationService>? _migrationLogger;
        private static bool _optimizeDatabaseAfterMigration;
        private static string? _restoreFromBackup;

        /// <summary>
        /// The entry point of the application.
        /// </summary>
        /// <param name="args">The command line arguments passed.</param>
        /// <returns><see cref="Task" />.</returns>
        public static Task Main(string[] args)
        {
            static Task ErrorParsingArguments(IEnumerable<Error> errors)
            {
                Environment.ExitCode = 1;
                return Task.CompletedTask;
            }

            // Parse the command line arguments and either start the app or exit indicating error
            return Parser.Default.ParseArguments<StartupOptions>(args)
                .MapResult(StartApp, ErrorParsingArguments);
        }

        private static async Task StartApp(StartupOptions options)
        {
            _restoreFromBackup = options.RestoreArchive;
            _startTimestamp = Stopwatch.GetTimestamp();
            ServerApplicationPaths appPaths = StartupHelpers.CreateApplicationPaths(options);
            appPaths.MakeSanityCheckOrThrow();

            // $KHYLO_LOG_DIR needs to be set for the logger configuration manager
            Environment.SetEnvironmentVariable("KHYLO_LOG_DIR", appPaths.LogDirectoryPath);

            // Enable cl-va P010 interop for tonemapping on Intel VAAPI
            Environment.SetEnvironmentVariable("NEOReadDebugKeys", "1");
            Environment.SetEnvironmentVariable("EnableExtendedVaFormats", "1");

            await StartupHelpers.InitLoggingConfigFile(appPaths).ConfigureAwait(false);

            // Create an instance of the application configuration to use for application startup
            IConfiguration startupConfig = CreateAppConfiguration(options, appPaths);
            StartupHelpers.InitializeLoggingFramework(startupConfig, appPaths);
            _setupServer = new SetupServer(static () => _khyloHost?.Services?.GetService<INetworkManager>(), appPaths, static () => _appHost, _loggerFactory, startupConfig);
            await _setupServer.RunAsync().ConfigureAwait(false);
            _logger = _loggerFactory.CreateLogger("Main");
            StartupLogger.Logger = new StartupLogger(_logger);

            // Use the logging framework for uncaught exceptions instead of std error
            AppDomain.CurrentDomain.UnhandledException += (_, e)
                => _logger.LogCritical((Exception)e.ExceptionObject, "Unhandled Exception");

            _logger.LogInformation(
                "Khylo Media Server v{Version} (Three Paws. No Paywalls.)",
                Assembly.GetEntryAssembly()!.GetName().Version!.ToString(3));

            StartupHelpers.LogEnvironmentInfo(_logger, appPaths);

            // If hosting the web client, validate the client content path
            if (startupConfig.HostWebClient())
            {
                var webContentPath = appPaths.WebPath;
                if (!Directory.Exists(webContentPath) || !Directory.EnumerateFiles(webContentPath).Any())
                {
                    _logger.LogError(
                        "The server is expected to host the web client, but the provided content directory is either " +
                        "invalid or empty: {WebContentPath}. If you do not want to host the web client with the " +
                        "server, you may set the '--nowebclient' command line flag, or set" +
                        "'{ConfigKey}=false' in your config settings",
                        webContentPath,
                        HostWebClientKey);
                    Environment.ExitCode = 1;
                    return;
                }
            }

            SetupServer.ReportActivity(StartupActivity.CheckingStorage);
            StorageHelper.TestCommonPathsForStorageCapacity(appPaths, StartupLogger.Logger.With(_loggerFactory.CreateLogger<Startup>()).BeginGroup($"Storage Check"));

            StartupHelpers.PerformStaticInitialization();

            SetupServer.ReportActivity(StartupActivity.Initializing);
            await ApplyStartupMigrationAsync(appPaths, startupConfig, options).ConfigureAwait(false);

            do
            {
                await StartServer(appPaths, options, startupConfig).ConfigureAwait(false);

                if (_restartOnShutdown)
                {
                    _startTimestamp = Stopwatch.GetTimestamp();
                    await _setupServer.StopAsync().ConfigureAwait(false);
                    await _setupServer.RunAsync().ConfigureAwait(false);
                }
            } while (_restartOnShutdown);

            _setupServer.Dispose();
        }

        private static async Task StartServer(IServerApplicationPaths appPaths, StartupOptions options, IConfiguration startupConfig)
        {
            using CoreAppHost appHost = new CoreAppHost(
                            appPaths,
                            _loggerFactory,
                            options,
                            startupConfig);
            var configurationCompleted = false;
            try
            {
                _khyloHost = Host.CreateDefaultBuilder()
                    .UseConsoleLifetime()
                    .ConfigureServices(services => appHost.Init(services))
                    .ConfigureWebHostDefaults(webHostBuilder =>
                    {
                        webHostBuilder.ConfigureWebHostBuilder(appHost, startupConfig, appPaths, _logger);
                        if (bool.TryParse(Environment.GetEnvironmentVariable("KHYLO_ENABLE_IIS"), out var iisEnabled) && iisEnabled)
                        {
                            _logger.LogCritical("UNSUPPORTED HOSTING ENVIRONMENT Microsoft Internet Information Services. The option to run Khylo on IIS is an unsupported and untested feature. Only use at your own discretion.");
                            webHostBuilder.UseIIS();
                        }
                    })
                    .ConfigureAppConfiguration(config => config.ConfigureAppConfiguration(options, appPaths, startupConfig))
                    .UseSerilog()
                    .ConfigureServices(e => e.RegisterStartupLogger())
                    .Build();

                /*
                 * Initialize the transcode path marker so we avoid starting Khylo in a broken state.
                 * This should really be a part of IApplicationPaths but this path is configured differently.
                 */
                _ = appHost.ConfigurationManager.GetTranscodePath();

                // Re-use the host service provider in the app host since ASP.NET doesn't allow a custom service collection.
                appHost.ServiceProvider = _khyloHost.Services;
                PrepareDatabaseProvider(appHost.ServiceProvider);

                if (!string.IsNullOrWhiteSpace(_restoreFromBackup))
                {
                    SetupServer.ReportActivity(StartupActivity.RestoringBackup);
                    await appHost.ServiceProvider.GetService<IBackupService>()!.RestoreBackupAsync(_restoreFromBackup).ConfigureAwait(false);
                    _restoreFromBackup = null;
                    _restartOnShutdown = true;
                    return;
                }

                var khyloMigrationService = ActivatorUtilities.CreateInstance<KhyloMigrationService>(appHost.ServiceProvider);
                SetupServer.ReportActivity(StartupActivity.PreparingMigrations);
                await khyloMigrationService.PrepareSystemForMigration(_logger).ConfigureAwait(false);
                // "Preparing migrations" carries through the DB read; per-migration progress is reported
                // as "Running migration X of Y" from inside the step once the pending set is known.
                _optimizeDatabaseAfterMigration |= await khyloMigrationService.MigrateStepAsync(KhyloMigrationStageTypes.CoreInitialisation, appHost.ServiceProvider).ConfigureAwait(false);

                SetupServer.ReportActivity(StartupActivity.InitializingServices);
                await appHost.InitializeServices(startupConfig).ConfigureAwait(false);
                _appHost = appHost;

                _optimizeDatabaseAfterMigration |= await khyloMigrationService.MigrateStepAsync(KhyloMigrationStageTypes.AppInitialisation, appHost.ServiceProvider).ConfigureAwait(false);
                await khyloMigrationService.CleanupSystemAfterMigration(_logger).ConfigureAwait(false);
                await OptimizeDatabaseAfterMigrationAsync(appHost.ServiceProvider).ConfigureAwait(false);
                try
                {
                    configurationCompleted = true;
                    await _setupServer!.StopAsync().ConfigureAwait(false);

                    if (options.StartupMode is null or Configuration.StartupMode.MediaServer)
                    {
                        await _khyloHost.StartAsync().ConfigureAwait(false);

                        if (!OperatingSystem.IsWindows() && startupConfig.UseUnixSocket())
                        {
                            var socketPath = StartupHelpers.GetUnixSocketPath(startupConfig, appPaths);

                            StartupHelpers.SetUnixSocketPermissions(startupConfig, socketPath, _logger);
                        }
                    }
                }
                catch (Exception)
                {
                    _logger.LogError("Kestrel failed to start! This is most likely due to an invalid address or port bind - correct your bind configuration in network.xml and try again");
                    throw;
                }

                if (options.StartupMode is null or Configuration.StartupMode.MediaServer)
                {
                    await appHost.RunStartupTasksAsync().ConfigureAwait(false);
                    _logger.LogInformation("Startup complete {Time:g}", Stopwatch.GetElapsedTime(_startTimestamp));

                    await _khyloHost.WaitForShutdownAsync().ConfigureAwait(false);
                }

                _restartOnShutdown = appHost.ShouldRestart;
                _restoreFromBackup = appHost.RestoreBackupPath;
            }
            catch (Exception ex)
            {
                _restartOnShutdown = false;
                _logger.LogCritical(ex, "Error while starting server");
                if (_setupServer!.IsAlive && !configurationCompleted)
                {
                    _setupServer!.SoftStop();
                    if (options.StartupMode is null or Configuration.StartupMode.MediaServer)
                    {
                        await Task.Delay(TimeSpan.FromMinutes(10)).ConfigureAwait(false);
                    }

                    await _setupServer!.StopAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                // Don't throw additional exception if startup failed.
                if (appHost.ServiceProvider is not null)
                {
                    _logger.LogInformation("Preparing the database for shutdown...");

                    var databaseProvider = appHost.ServiceProvider.GetRequiredService<IKhyloDatabaseProvider>();
                    await databaseProvider.RunShutdownTask(CancellationToken.None).ConfigureAwait(false);
                }

                _appHost = null;
                _khyloHost?.Dispose();
                _khyloHost = null;
            }
        }

        /// <summary>
        /// [Internal]Runs the startup Migrations.
        /// </summary>
        /// <remarks>
        /// Not intended to be used other then by khylo and its tests.
        /// </remarks>
        /// <param name="appPaths">Application Paths.</param>
        /// <param name="startupConfig">Startup Config.</param>
        /// <param name="startupOptions">The applications startup options.</param>
        /// <returns>A task.</returns>
        public static async Task ApplyStartupMigrationAsync(ServerApplicationPaths appPaths, IConfiguration startupConfig, StartupOptions startupOptions)
        {
            _migrationLogger = StartupLogger.Logger.BeginGroup<KhyloMigrationService>($"Migration Service");
            var startupConfigurationManager = new ServerConfigurationManager(appPaths, _loggerFactory, new MyXmlSerializer());
            startupConfigurationManager.AddParts([new DatabaseConfigurationFactory()]);
            var migrationStartupServiceProvider = new ServiceCollection()
                .AddLogging(d => d.AddSerilog())
                .AddKhyloDbContext(startupConfigurationManager, startupConfig)
                .AddSingleton<IApplicationPaths>(appPaths)
                .AddSingleton<ServerApplicationPaths>(appPaths)
                .RegisterStartupLogger();

            var startupService = migrationStartupServiceProvider.BuildServiceProvider();

            PrepareDatabaseProvider(startupService);

            var khyloMigrationService = ActivatorUtilities.CreateInstance<KhyloMigrationService>(startupService);
            await khyloMigrationService.CheckFirstTimeRunOrMigration(appPaths, startupOptions).ConfigureAwait(false);
            _optimizeDatabaseAfterMigration |= await khyloMigrationService.MigrateStepAsync(Migrations.Stages.KhyloMigrationStageTypes.PreInitialisation, startupService).ConfigureAwait(false);
        }

        /// <summary>
        /// [Internal]Runs the Khylo migrator service with the Core stage.
        /// </summary>
        /// <remarks>
        /// Not intended to be used other then by khylo and its tests.
        /// </remarks>
        /// <param name="serviceProvider">The service provider.</param>
        /// <param name="khyloMigrationStage">The stage to run.</param>
        /// <returns>A task.</returns>
        public static async Task ApplyCoreMigrationsAsync(IServiceProvider serviceProvider, Migrations.Stages.KhyloMigrationStageTypes khyloMigrationStage)
        {
            var khyloMigrationService = ActivatorUtilities.CreateInstance<KhyloMigrationService>(serviceProvider, _migrationLogger!);
            _optimizeDatabaseAfterMigration |= await khyloMigrationService.MigrateStepAsync(khyloMigrationStage, serviceProvider).ConfigureAwait(false);
        }

        private static async Task OptimizeDatabaseAfterMigrationAsync(IServiceProvider serviceProvider)
        {
            if (!_optimizeDatabaseAfterMigration)
            {
                return;
            }

            // Reset first: a restart runs no migrations and must not optimize again.
            _optimizeDatabaseAfterMigration = false;
            SetupServer.ReportActivity(StartupActivity.OptimizingDatabase);
            _logger.LogInformation("Migrations have been applied, optimizing the database... This might take a while");

            try
            {
                // Deliberately untimed: incomplete statistics are worse than a slow start.
                var databaseProvider = serviceProvider.GetRequiredService<IKhyloDatabaseProvider>();
                await databaseProvider.RunScheduledOptimisation(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // A missed optimization only costs performance, so never fail startup over this.
                _logger.LogError(ex, "Error while optimizing the database after migration");
            }
        }

        /// <summary>
        /// Create the application configuration.
        /// </summary>
        /// <param name="commandLineOpts">The command line options passed to the program.</param>
        /// <param name="appPaths">The application paths.</param>
        /// <returns>The application configuration.</returns>
        public static IConfiguration CreateAppConfiguration(StartupOptions commandLineOpts, IApplicationPaths appPaths)
        {
            return new ConfigurationBuilder()
                .ConfigureAppConfiguration(commandLineOpts, appPaths)
                .Build();
        }

        private static IConfigurationBuilder ConfigureAppConfiguration(
            this IConfigurationBuilder config,
            StartupOptions commandLineOpts,
            IApplicationPaths appPaths,
            IConfiguration? startupConfig = null)
        {
            // Use the swagger API page as the default redirect path if not hosting the web client
            var inMemoryDefaultConfig = ConfigurationOptions.DefaultConfiguration;
            if (startupConfig is not null && !startupConfig.HostWebClient())
            {
                inMemoryDefaultConfig[DefaultRedirectKey] = "api-docs/swagger";
            }

            return config
                .SetBasePath(appPaths.ConfigurationDirectoryPath)
                .AddInMemoryCollection(inMemoryDefaultConfig)
                .AddJsonFile(LoggingConfigFileDefault, optional: false, reloadOnChange: true)
                .AddJsonFile(LoggingConfigFileSystem, optional: true, reloadOnChange: true)
                .AddEnvironmentVariables("KHYLO_")
                .AddInMemoryCollection(commandLineOpts.ConvertToConfig());
        }

        private static void PrepareDatabaseProvider(IServiceProvider services)
        {
            var factory = services.GetRequiredService<IDbContextFactory<KhyloDbContext>>();
            var provider = services.GetRequiredService<IKhyloDatabaseProvider>();
            provider.DbContextFactory = factory;
        }
    }
}
