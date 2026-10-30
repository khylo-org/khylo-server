using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Controller.Configuration;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Migrations.Routines;

/// <summary>
/// Switches the online music sources (MusicBrainz and TheAudioDB) back on server-wide. With them off,
/// artists and albums whose files carry no metadata of their own never get a photo, bio or genres.
/// Local metadata still comes first; the online sources only fill in what's missing.
/// </summary>
[KhyloMigration("2026-10-16T12:00:00", nameof(EnableOnlineMusicMetadata), Stage = Stages.KhyloMigrationStageTypes.CoreInitialisation)]
public class EnableOnlineMusicMetadata : IAsyncMigrationRoutine
{
    private static readonly string[] _musicTypes = ["MusicAlbum", "MusicArtist", "Audio"];
    private static readonly string[] _musicSources = ["MusicBrainz", "TheAudioDB"];

    private readonly IServerConfigurationManager _serverConfigurationManager;
    private readonly ILogger<EnableOnlineMusicMetadata> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EnableOnlineMusicMetadata"/> class.
    /// </summary>
    /// <param name="serverConfigurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
    /// <param name="logger">The logger.</param>
    public EnableOnlineMusicMetadata(IServerConfigurationManager serverConfigurationManager, ILogger<EnableOnlineMusicMetadata> logger)
    {
        _serverConfigurationManager = serverConfigurationManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task PerformAsync(CancellationToken cancellationToken)
    {
        foreach (var options in _serverConfigurationManager.Configuration.MetadataOptions
            .Where(o => _musicTypes.Contains(o.ItemType, StringComparer.OrdinalIgnoreCase)))
        {
            options.DisabledMetadataFetchers = options.DisabledMetadataFetchers.Except(_musicSources, StringComparer.OrdinalIgnoreCase).ToArray();
            options.DisabledImageFetchers = options.DisabledImageFetchers.Except(_musicSources, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        _serverConfigurationManager.SaveConfiguration();
        _logger.LogInformation("Turned on online music metadata (MusicBrainz, TheAudioDB)");

        return Task.CompletedTask;
    }
}
