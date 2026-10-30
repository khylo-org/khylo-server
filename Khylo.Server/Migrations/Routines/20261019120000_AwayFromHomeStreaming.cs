using System.Threading;
using System.Threading.Tasks;
using Khylo.Controller.Configuration;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Migrations.Routines;

/// <summary>
/// Gives servers with no away-from-home video limit Khylo's default of 8 Mbps, so movies and shows
/// still start and keep playing on a phone signal. Admins can change it in Playback settings.
/// </summary>
[KhyloMigration("2026-10-19T12:00:00", nameof(AwayFromHomeStreaming), Stage = Stages.KhyloMigrationStageTypes.CoreInitialisation)]
public class AwayFromHomeStreaming : IAsyncMigrationRoutine
{
    private readonly IServerConfigurationManager _serverConfigurationManager;
    private readonly ILogger<AwayFromHomeStreaming> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AwayFromHomeStreaming"/> class.
    /// </summary>
    /// <param name="serverConfigurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
    /// <param name="logger">The logger.</param>
    public AwayFromHomeStreaming(IServerConfigurationManager serverConfigurationManager, ILogger<AwayFromHomeStreaming> logger)
    {
        _serverConfigurationManager = serverConfigurationManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task PerformAsync(CancellationToken cancellationToken)
    {
        if (_serverConfigurationManager.Configuration.RemoteClientBitrateLimit <= 0)
        {
            _serverConfigurationManager.Configuration.RemoteClientBitrateLimit = 8_000_000;
            _serverConfigurationManager.SaveConfiguration();
            _logger.LogInformation("Set the away-from-home video limit to 8 Mbps");
        }

        return Task.CompletedTask;
    }
}
