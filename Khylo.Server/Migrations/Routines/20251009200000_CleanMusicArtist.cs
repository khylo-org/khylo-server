using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Data.Enums;
using Khylo.Database.Implementations;
using Khylo.Server.ServerSetupApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Migrations.Routines;

/// <summary>
/// Cleans up all Music artists that have been migrated in the 10.11 RC migrations.
/// </summary>
[KhyloMigration("2025-10-09T20:00:00", nameof(CleanMusicArtist))]
[KhyloMigrationBackup(KhyloDb = true)]
public class CleanMusicArtist : IAsyncMigrationRoutine
{
    private readonly IStartupLogger<CleanMusicArtist> _startupLogger;
    private readonly IDbContextFactory<KhyloDbContext> _dbContextFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="CleanMusicArtist"/> class.
    /// </summary>
    /// <param name="startupLogger">The startup logger.</param>
    /// <param name="dbContextFactory">The Db context factory.</param>
    public CleanMusicArtist(IStartupLogger<CleanMusicArtist> startupLogger, IDbContextFactory<KhyloDbContext> dbContextFactory)
    {
        _startupLogger = startupLogger;
        _dbContextFactory = dbContextFactory;
    }

    /// <inheritdoc/>
    public async Task PerformAsync(CancellationToken cancellationToken)
    {
        var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var peoples = context.Peoples.Where(e => e.PersonType == nameof(PersonKind.Artist) || e.PersonType == nameof(PersonKind.AlbumArtist));
            _startupLogger.LogInformation("Delete {Number} Artist and Album Artist person types from db", await peoples.CountAsync(cancellationToken).ConfigureAwait(false));

            await peoples
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
