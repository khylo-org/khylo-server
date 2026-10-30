using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Khylo.Server.Core.Library;

/// <summary>
/// Checks the storage behind every library once a minute. When a location drops (a NAS or file server
/// rebooting, say), Khylo tries to reconnect it, and once it's back, folder watching is started again so new
/// media keeps appearing by itself. The dashboard shows what can't be reached.
/// </summary>
public sealed class StorageMonitor : BackgroundService
{
    private static readonly TimeSpan _interval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    private readonly ILibraryManager _libraryManager;
    private readonly ILibraryMonitor _libraryMonitor;
    private readonly ILogger<StorageMonitor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageMonitor"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="libraryMonitor">The library monitor.</param>
    /// <param name="logger">The logger.</param>
    public StorageMonitor(ILibraryManager libraryManager, ILibraryMonitor libraryMonitor, ILogger<StorageMonitor> logger)
    {
        _libraryManager = libraryManager;
        _libraryMonitor = libraryMonitor;
        _logger = logger;
        StorageHealth.Reconnect = TryReconnect;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let startup finish first
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                CheckAll();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Couldn't check library storage");
            }

            await Task.Delay(_interval, stoppingToken).ConfigureAwait(false);
        }
    }

    private void CheckAll()
    {
        var locations = _libraryManager.GetVirtualFolders()
            .SelectMany(f => (f.Locations ?? []).Select(path => (Library: f.Name ?? string.Empty, Path: path)))
            .ToList();
        StorageHealth.Keep(locations.Select(l => l.Path));

        var cameBack = false;
        foreach (var (library, path) in locations)
        {
            var reachable = StorageHealth.IsReachable(path, _timeout);
            if (!reachable && TryReconnect(path))
            {
                reachable = StorageHealth.IsReachable(path, _timeout);
            }

            if (!StorageHealth.Record(library, path, reachable))
            {
                continue;
            }

            if (reachable)
            {
                _logger.LogInformation("Storage for {Library} is reachable again: {Path}", library, path);
                cameBack = true;
            }
            else
            {
                _logger.LogWarning("Storage for {Library} can't be reached: {Path}. Khylo will keep trying to reconnect it", library, path);
            }
        }

        // Folder watchers on a share that dropped are gone; start them again so new media shows up by itself
        if (cameBack)
        {
            _libraryMonitor.Stop();
            _libraryMonitor.Start();
            _logger.LogInformation("Restarted folder watching after storage came back");
        }
    }

    // A mapped network drive (X:) stays disconnected after its server reboots until something restores it;
    // this asks Windows to restore it with the saved connection, as File Explorer does when it's opened.
    private bool TryReconnect(string path)
    {
        if (!OperatingSystem.IsWindows() || path.Length < 2 || path[1] != ':')
        {
            return false;
        }

        var drive = path[..2].ToUpperInvariant();
        try
        {
            if (new DriveInfo(drive).DriveType != DriveType.Network && Directory.Exists(drive + "\\"))
            {
                return false;
            }
        }
        catch (ArgumentException)
        {
            return false;
        }

        var result = NativeMethods.WNetRestoreSingleConnectionW(IntPtr.Zero, drive, false);
        if (result == 0)
        {
            _logger.LogInformation("Reconnected network drive {Drive}", drive);
            return true;
        }

        _logger.LogDebug("Couldn't reconnect network drive {Drive} (error {Error})", drive, result);
        return false;
    }

    private static class NativeMethods
    {
        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int WNetRestoreSingleConnectionW(IntPtr hwndParent, string lpDevice, [MarshalAs(UnmanagedType.Bool)] bool fUseUI);
    }
}
