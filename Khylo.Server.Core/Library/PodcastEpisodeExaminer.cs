using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Controller.Entities;
using Khylo.Controller.Library;
using Khylo.Controller.Providers;
using Khylo.Data.Enums;
using Khylo.Model.IO;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Core.Library;

/// <summary>
/// Podcast episodes added before Khylo examined them have no length or audio details, so they can't play. Shortly
/// after startup this asks for each of those to be examined once; afterwards new episodes are examined as they're added.
/// </summary>
public sealed class PodcastEpisodeExaminer : BackgroundService
{
    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<PodcastEpisodeExaminer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PodcastEpisodeExaminer"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="providerManager">The provider manager.</param>
    /// <param name="fileSystem">The file system.</param>
    /// <param name="logger">The logger.</param>
    public PodcastEpisodeExaminer(ILibraryManager libraryManager, IProviderManager providerManager, IFileSystem fileSystem, ILogger<PodcastEpisodeExaminer> logger)
    {
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let startup and the first library scan settle
        await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken).ConfigureAwait(false);

        var unexamined = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.PodcastEpisode],
            Recursive = true,
            IsVirtualItem = false
        }).Where(i => i.RunTimeTicks is null or 0).ToList();

        if (unexamined.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Examining {Count} podcast episodes so they can play", unexamined.Count);
        foreach (var item in unexamined)
        {
            _providerManager.QueueRefresh(
                item.Id,
                new MetadataRefreshOptions(new DirectoryService(_fileSystem)) { MetadataRefreshMode = MetadataRefreshMode.FullRefresh },
                RefreshPriority.Low);
        }
    }
}
