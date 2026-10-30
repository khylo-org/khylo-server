using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Database.Implementations;
using Khylo.Database.Implementations.Enums;
using Khylo.Server.Migrations.Stages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Migrations.Routines;

/// <summary>
/// Turns subtitles off by default for existing users. Khylo's "Default" mode switches subtitles
/// on whenever a file has an external subtitle file or a track flagged as default, which is most
/// files; Khylo only shows forced subtitles unless the viewer picks a track. Users can still choose
/// any mode in their playback settings afterwards.
/// </summary>
[KhyloMigration("2026-10-08T12:00:00", nameof(SubtitlesOffByDefault), Stage = KhyloMigrationStageTypes.CoreInitialisation)]
internal class SubtitlesOffByDefault : IAsyncMigrationRoutine
{
    private readonly IDbContextFactory<KhyloDbContext> _contextFactory;
    private readonly ILogger<SubtitlesOffByDefault> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubtitlesOffByDefault"/> class.
    /// </summary>
    /// <param name="contextFactory">The database context factory.</param>
    /// <param name="logger">The logger.</param>
    public SubtitlesOffByDefault(IDbContextFactory<KhyloDbContext> contextFactory, ILogger<SubtitlesOffByDefault> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task PerformAsync(CancellationToken cancellationToken)
    {
        var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var updated = await dbContext.Users
                .Where(u => u.SubtitleMode == SubtitlePlaybackMode.Default)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.SubtitleMode, SubtitlePlaybackMode.OnlyForced), cancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation("Turned subtitles off by default for {Count} users", updated);
        }
    }
}
