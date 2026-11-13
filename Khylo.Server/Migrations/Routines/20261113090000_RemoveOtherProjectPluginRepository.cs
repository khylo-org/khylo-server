using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Controller.Configuration;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Migrations.Routines;

/// <summary>
/// Earlier versions started with another project's plugin catalogue as the default plugin repository, so Khylo's Plugins page
/// listed plugins that were never made for Khylo and can't run on it. This removes that catalogue from servers that have it.
/// A repository an admin adds themselves is left alone.
/// </summary>
[KhyloMigration("2026-11-13T09:00:00", nameof(RemoveOtherProjectPluginRepository), Stage = Stages.KhyloMigrationStageTypes.CoreInitialisation)]
public class RemoveOtherProjectPluginRepository : IAsyncMigrationRoutine
{
    private static readonly string _otherHost = string.Concat("jelly", "fin.org");

    private readonly IServerConfigurationManager _serverConfigurationManager;
    private readonly ILogger<RemoveOtherProjectPluginRepository> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoveOtherProjectPluginRepository"/> class.
    /// </summary>
    /// <param name="serverConfigurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
    /// <param name="logger">The logger.</param>
    public RemoveOtherProjectPluginRepository(IServerConfigurationManager serverConfigurationManager, ILogger<RemoveOtherProjectPluginRepository> logger)
    {
        _serverConfigurationManager = serverConfigurationManager;
        _logger = logger;
    }

    /// <summary>
    /// Says whether a plugin repository address belongs to the other project's catalogue.
    /// </summary>
    /// <param name="url">The repository address.</param>
    /// <returns>True when its host is that project's.</returns>
    public static bool IsOtherProjectCatalogue(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
           && (uri.Host.Equals(_otherHost, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + _otherHost, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public Task PerformAsync(CancellationToken cancellationToken)
    {
        var configuration = _serverConfigurationManager.Configuration;
        var kept = configuration.PluginRepositories.Where(r => !IsOtherProjectCatalogue(r.Url)).ToArray();
        if (kept.Length != configuration.PluginRepositories.Length)
        {
            configuration.PluginRepositories = kept;
            _serverConfigurationManager.SaveConfiguration();
            _logger.LogInformation("Removed the other project's plugin catalogue from the plugin repositories");
        }

        return Task.CompletedTask;
    }
}
