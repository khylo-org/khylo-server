using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Common.Configuration;
using Khylo.Controller.Library;
using Khylo.Controller.Persistence;
using Khylo.Data.Enums;
using Khylo.Database.Implementations;
using Khylo.Database.Implementations.Entities;
using Khylo.Model.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Core.ScheduledTasks.Tasks;

/// <summary>
/// Finds artists and albums that are still missing a photo, bio or cover and haven't been looked up in
/// the last month, and hands them to the background music lookup (which keeps every online source within
/// its limits). Only the missing parts are filled in; nothing from the user's files is replaced.
/// </summary>
public class KhyloMusicDetailsTask : IScheduledTask, IConfigurableScheduledTask
{
    private readonly IDbContextFactory<KhyloDbContext> _dbContextFactory;
    private readonly IItemTypeLookup _itemTypeLookup;
    private readonly IApplicationPaths _paths;
    private readonly ILogger<KhyloMusicDetailsTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="KhyloMusicDetailsTask"/> class.
    /// </summary>
    /// <param name="dbContextFactory">The database context factory.</param>
    /// <param name="itemTypeLookup">The item type lookup.</param>
    /// <param name="paths">The application paths.</param>
    /// <param name="logger">The logger.</param>
    public KhyloMusicDetailsTask(
        IDbContextFactory<KhyloDbContext> dbContextFactory,
        IItemTypeLookup itemTypeLookup,
        IApplicationPaths paths,
        ILogger<KhyloMusicDetailsTask> logger)
    {
        _dbContextFactory = dbContextFactory;
        _itemTypeLookup = itemTypeLookup;
        _paths = paths;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Fill in missing music details";

    /// <inheritdoc />
    public string Description => "Looks up artist photos, bios and album covers that are still missing, in the background and within each online source's limits, without replacing anything from your files.";

    /// <inheritdoc />
    public string Category => "Library";

    /// <inheritdoc />
    public string Key => "KhyloMusicDetails";

    /// <inheritdoc />
    public bool IsHidden => false;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsLogged => true;

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Every night after the 3 am library scan, and each time the server starts (after an update)
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.DailyTrigger,
            TimeOfDayTicks = TimeSpan.FromHours(4).Ticks
        };

        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.StartupTrigger
        };
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var artistType = _itemTypeLookup.BaseItemKindNames[BaseItemKind.MusicArtist];
        var albumType = _itemTypeLookup.BaseItemKindNames[BaseItemKind.MusicAlbum];

        List<Guid> ids;
        var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            // Artists missing a photo or bio, albums missing a cover
            ids = await context.BaseItems
                .AsNoTracking()
                .Where(b => b.Type == artistType || b.Type == albumType)
                .Where(b => !b.IsLocked)
                .Where(b => !b.Images!.Any(i => i.ImageType == ImageInfoImageType.Primary)
                    || (b.Type == artistType && string.IsNullOrEmpty(b.Overview)))
                .Select(b => b.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        // Ones looked up in the last month are left alone: online sources rarely change that fast
        var due = ids.Where(id => !MusicEnrichment.WasCheckedRecently(_paths.CachePath, id)).ToList();
        foreach (var id in due)
        {
            MusicEnrichment.Enqueue(id, urgent: false);
        }

        _logger.LogInformation("Queued {Count} artists and albums for music details ({Skipped} were looked up recently)", due.Count, ids.Count - due.Count);
        progress.Report(100);
    }
}
