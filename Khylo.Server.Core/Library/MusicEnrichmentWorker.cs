using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Common.Configuration;
using Khylo.Controller.Entities;
using Khylo.Controller.Entities.Audio;
using Khylo.Controller.Library;
using Khylo.Controller.Providers;
using Khylo.Model.Entities;
using Khylo.Model.IO;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Core.Library;

/// <summary>
/// Looks up music details (bios, photos, covers) in the background, one artist or album at a time,
/// after library scans have added them. Artists and albums someone opens go first. Each online source
/// keeps to its own request limits, so a big library fills in steadily instead of all at once.
/// </summary>
public sealed class MusicEnrichmentWorker : BackgroundService
{
    private const int BatchSize = 25;

    private readonly ILibraryManager _libraryManager;
    private readonly IFileSystem _fileSystem;
    private readonly IApplicationPaths _paths;
    private readonly ILogger<MusicEnrichmentWorker> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MusicEnrichmentWorker"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="fileSystem">The file system.</param>
    /// <param name="paths">The application paths.</param>
    /// <param name="logger">The logger.</param>
    public MusicEnrichmentWorker(ILibraryManager libraryManager, IFileSystem fileSystem, IApplicationPaths paths, ILogger<MusicEnrichmentWorker> logger)
    {
        _libraryManager = libraryManager;
        _fileSystem = fileSystem;
        _paths = paths;
        _logger = logger;
    }

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        MusicEnrichment.IsScanRunning = () => _libraryManager.IsScanRunning;

        // New artists and albums get their details once a scan has added them
        _libraryManager.ItemAdded += OnItemAdded;
        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemAdded;
        return base.StopAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var ids = await MusicEnrichment.TakeAsync(BatchSize, stoppingToken).ConfigureAwait(false);
                var items = ids.Select(id => _libraryManager.GetItemById(id)).Where(i => i is MusicArtist or MusicAlbum).ToList();

                // Sources that answer for many artists at once can do this whole batch in one request
                MusicEnrichment.UpcomingArtistNames = items.OfType<MusicArtist>().Select(a => a.Name).ToList();

                foreach (var item in items)
                {
                    await FillInAsync(item!, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Music details lookup failed");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task FillInAsync(BaseItem item, CancellationToken cancellationToken)
    {
        try
        {
            var hasImage = item.HasImage(ImageType.Primary, 0);
            var options = new MetadataRefreshOptions(new DirectoryService(_fileSystem))
            {
                MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                ImageRefreshMode = hasImage ? MetadataRefreshMode.ValidationOnly : MetadataRefreshMode.FullRefresh,
                ReplaceAllMetadata = false,
                ReplaceAllImages = false
            };

            using (MusicEnrichment.Begin())
            {
                await item.RefreshMetadata(options, cancellationToken).ConfigureAwait(false);
            }

            MusicEnrichment.MarkChecked(_paths.CachePath, item.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Couldn't look up details for {Name}", item.Name);
        }
    }

    private void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        if (e.Item is MusicArtist or MusicAlbum)
        {
            MusicEnrichment.Enqueue(e.Item.Id, urgent: false);
        }
    }
}
