using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Khylo.Common.Configuration;
using Khylo.Controller.Configuration;
using Khylo.Database.Implementations;
using Khylo.Database.Implementations.DbConfiguration;
using Khylo.Database.Implementations.Locking;
using Khylo.Database.Providers.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using KhyloDbProviderFactory = System.Func<System.IServiceProvider, Khylo.Database.Implementations.IKhyloDatabaseProvider>;

namespace Khylo.Server.Implementations.Extensions;

/// <summary>
/// Extensions for the <see cref="IServiceCollection"/> interface.
/// </summary>
public static class ServiceCollectionExtensions
{
    private static IEnumerable<Type> DatabaseProviderTypes()
    {
        yield return typeof(SqliteDatabaseProvider);
    }

    private static IDictionary<string, KhyloDbProviderFactory> GetSupportedDbProviders()
    {
        var items = new Dictionary<string, KhyloDbProviderFactory>(StringComparer.InvariantCultureIgnoreCase);
        foreach (var providerType in DatabaseProviderTypes())
        {
            var keyAttribute = providerType.GetCustomAttribute<KhyloDatabaseProviderKeyAttribute>();
            if (keyAttribute is null || string.IsNullOrWhiteSpace(keyAttribute.DatabaseProviderKey))
            {
                continue;
            }

            var provider = providerType;
            items[keyAttribute.DatabaseProviderKey] = (services) => (IKhyloDatabaseProvider)ActivatorUtilities.CreateInstance(services, providerType);
        }

        return items;
    }

    private static KhyloDbProviderFactory? LoadDatabasePlugin(CustomDatabaseOptions customProviderOptions, IApplicationPaths applicationPaths)
    {
        var plugin = Directory.EnumerateDirectories(applicationPaths.PluginsPath)
            .Where(e => Path.GetFileName(e)!.StartsWith(customProviderOptions.PluginName, StringComparison.OrdinalIgnoreCase))
            .Order()
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"The requested custom database plugin with the name '{customProviderOptions.PluginName}' could not been found in '{applicationPaths.PluginsPath}'");

        var dbProviderAssembly = Path.Combine(plugin, Path.ChangeExtension(customProviderOptions.PluginAssembly, "dll"));
        if (!File.Exists(dbProviderAssembly))
        {
            throw new InvalidOperationException($"Could not find the requested assembly at '{dbProviderAssembly}'");
        }

        // we have to load the assembly without proxy to ensure maximum performance for this.
        var assembly = Assembly.LoadFrom(dbProviderAssembly);
        var dbProviderType = assembly.GetExportedTypes().FirstOrDefault(f => f.IsAssignableTo(typeof(IKhyloDatabaseProvider)))
            ?? throw new InvalidOperationException($"Could not find any type implementing the '{nameof(IKhyloDatabaseProvider)}' interface.");

        return (services) => (IKhyloDatabaseProvider)ActivatorUtilities.CreateInstance(services, dbProviderType);
    }

    /// <summary>
    /// Adds the <see cref="IDbContextFactory{TContext}"/> interface to the service collection with second level caching enabled.
    /// </summary>
    /// <param name="serviceCollection">An instance of the <see cref="IServiceCollection"/> interface.</param>
    /// <param name="configurationManager">The server configuration manager.</param>
    /// <param name="configuration">The startup Configuration.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddKhyloDbContext(
        this IServiceCollection serviceCollection,
        IServerConfigurationManager configurationManager,
        IConfiguration configuration)
    {
        var efCoreConfiguration = configurationManager.GetConfiguration<DatabaseConfigurationOptions>("database");
        KhyloDbProviderFactory? providerFactory = null;

        if (efCoreConfiguration?.DatabaseType is null)
        {
            var cmdMigrationArgument = configuration.GetValue<string>("migration-provider");
            if (!string.IsNullOrWhiteSpace(cmdMigrationArgument))
            {
                efCoreConfiguration = new DatabaseConfigurationOptions()
                {
                    DatabaseType = cmdMigrationArgument,
                };
            }
            else
            {
                // when nothing is setup via new Database configuration, fallback to SQLite with default settings.
                efCoreConfiguration = new DatabaseConfigurationOptions()
                {
                    DatabaseType = "Khylo-SQLite",
                    LockingBehavior = DatabaseLockingBehaviorTypes.NoLock
                };
                configurationManager.SaveConfiguration("database", efCoreConfiguration);
            }
        }

        if (efCoreConfiguration.DatabaseType.Equals("PLUGIN_PROVIDER", StringComparison.OrdinalIgnoreCase))
        {
            if (efCoreConfiguration.CustomProviderOptions is null)
            {
                throw new InvalidOperationException("The custom database provider must declare the custom provider options to work");
            }

            providerFactory = LoadDatabasePlugin(efCoreConfiguration.CustomProviderOptions, configurationManager.ApplicationPaths);
        }
        else
        {
            var providers = GetSupportedDbProviders();
            if (!providers.TryGetValue(efCoreConfiguration.DatabaseType.ToUpperInvariant(), out providerFactory!))
            {
                throw new InvalidOperationException($"Khylo cannot find the database provider of type '{efCoreConfiguration.DatabaseType}'. Supported types are {string.Join(", ", providers.Keys)}");
            }
        }

        serviceCollection.AddSingleton<IKhyloDatabaseProvider>(providerFactory!);

        switch (efCoreConfiguration.LockingBehavior)
        {
            case DatabaseLockingBehaviorTypes.NoLock:
                serviceCollection.AddSingleton<IEntityFrameworkCoreLockingBehavior, NoLockBehavior>();
                break;
            case DatabaseLockingBehaviorTypes.Pessimistic:
                serviceCollection.AddSingleton<IEntityFrameworkCoreLockingBehavior, PessimisticLockBehavior>();
                break;
            case DatabaseLockingBehaviorTypes.Optimistic:
                serviceCollection.AddSingleton<IEntityFrameworkCoreLockingBehavior, OptimisticLockBehavior>();
                break;
        }

        serviceCollection.AddPooledDbContextFactory<KhyloDbContext>((serviceProvider, opt) =>
        {
            var provider = serviceProvider.GetRequiredService<IKhyloDatabaseProvider>();
            provider.Initialise(opt, efCoreConfiguration);
            var lockingBehavior = serviceProvider.GetRequiredService<IEntityFrameworkCoreLockingBehavior>();
            lockingBehavior.Initialise(opt);
        });

        return serviceCollection;
    }
}
