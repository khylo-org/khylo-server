using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Khylo.Controller.Library;

/// <summary>
/// Khylo fills in music details from online sources (bios, photos, covers) in the background, not during
/// library scans: a scan only reads the files, so new music shows up in minutes, and a slow or busy
/// online source can never hold it up. Artists and albums are queued here and looked up one by one by
/// a background worker; ones someone opens jump the queue.
/// </summary>
public static class MusicEnrichment
{
    private static readonly AsyncLocal<bool> _active = new();
    private static readonly ConcurrentQueue<Guid> _urgent = new();
    private static readonly ConcurrentQueue<Guid> _normal = new();
    private static readonly ConcurrentDictionary<Guid, byte> _queued = new();
    private static readonly SemaphoreSlim _signal = new(0);
    private static readonly TimeSpan _recheckAfter = TimeSpan.FromDays(30);
    private static readonly object _fileLock = new();
    private static ConcurrentDictionary<Guid, DateTime>? _checked;
    private static string? _checkedPath;

    /// <summary>
    /// Gets or sets a check for whether a library scan is running (set by the worker at startup).
    /// </summary>
    public static Func<bool>? IsScanRunning { get; set; }

    /// <summary>
    /// Gets a value indicating whether music providers may go online right now: always in the background
    /// worker, and outside library scans (a manual refresh, for example). During a scan they only use what
    /// they've already got.
    /// </summary>
    public static bool MayGoOnline => _active.Value || !(IsScanRunning?.Invoke() ?? false);

    /// <summary>
    /// Gets or sets the names of the artists the worker is about to look up, so sources that can answer for
    /// many artists in one request (Wikidata, Wikipedia) can do them together.
    /// </summary>
    public static IReadOnlyList<string> UpcomingArtistNames { get; set; } = [];

    /// <summary>
    /// Gets how many are waiting.
    /// </summary>
    public static int Pending => _queued.Count;

    /// <summary>
    /// Marks the current work as the background lookup, allowing online lookups even during a scan.
    /// </summary>
    /// <returns>Dispose to end it.</returns>
    public static IDisposable Begin()
    {
        _active.Value = true;
        return new EndScope();
    }

    /// <summary>
    /// Queues an artist or album for lookup.
    /// </summary>
    /// <param name="id">The item.</param>
    /// <param name="urgent">Whether someone is looking at it now.</param>
    public static void Enqueue(Guid id, bool urgent)
    {
        if (!_queued.TryAdd(id, 0))
        {
            if (urgent)
            {
                // Already waiting: move it up
                _urgent.Enqueue(id);
                _signal.Release();
            }

            return;
        }

        (urgent ? _urgent : _normal).Enqueue(id);
        _signal.Release();
    }

    /// <summary>
    /// Waits for the next items to look up, urgent ones first.
    /// </summary>
    /// <param name="max">The most to take at once.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The items.</returns>
    public static async Task<List<Guid>> TakeAsync(int max, CancellationToken cancellationToken)
    {
        await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
        var taken = new List<Guid>();
        while (taken.Count < max && (_urgent.TryDequeue(out var id) || _normal.TryDequeue(out id)))
        {
            if (_queued.TryRemove(id, out _))
            {
                taken.Add(id);
            }
        }

        // Keep the signal count roughly in step with what's left
        for (var i = 1; i < taken.Count && _signal.CurrentCount > 0; i++)
        {
            await _signal.WaitAsync(0, cancellationToken).ConfigureAwait(false);
        }

        return taken;
    }

    /// <summary>
    /// Whether an item was looked up recently enough to leave alone.
    /// </summary>
    /// <param name="cachePath">The server's cache folder.</param>
    /// <param name="id">The item.</param>
    /// <returns>True when it was.</returns>
    public static bool WasCheckedRecently(string cachePath, Guid id)
        => LoadChecked(cachePath).TryGetValue(id, out var when) && DateTime.UtcNow - when < _recheckAfter;

    /// <summary>
    /// Records that an item was looked up (a short line per item in the cache folder).
    /// </summary>
    /// <param name="cachePath">The server's cache folder.</param>
    /// <param name="id">The item.</param>
    public static void MarkChecked(string cachePath, Guid id)
    {
        var map = LoadChecked(cachePath);
        var now = DateTime.UtcNow;
        map[id] = now;
        lock (_fileLock)
        {
            try
            {
                File.AppendAllText(_checkedPath!, string.Create(CultureInfo.InvariantCulture, $"{id:N}\t{now.Ticks}\n"));
            }
            catch (IOException)
            {
                // Only saves time; worst case the item is looked up again
            }
        }
    }

    private static ConcurrentDictionary<Guid, DateTime> LoadChecked(string cachePath)
    {
        if (_checked is not null)
        {
            return _checked;
        }

        lock (_fileLock)
        {
            if (_checked is not null)
            {
                return _checked;
            }

            _checkedPath = Path.Combine(cachePath, "khylo-music-checked.tsv");
            var map = new ConcurrentDictionary<Guid, DateTime>();
            var lines = 0;
            try
            {
                if (File.Exists(_checkedPath))
                {
                    foreach (var line in File.ReadLines(_checkedPath))
                    {
                        lines++;
                        var parts = line.Split('\t');
                        if (parts.Length == 2 && Guid.TryParse(parts[0], out var id) && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks))
                        {
                            var when = new DateTime(ticks, DateTimeKind.Utc);
                            if (DateTime.UtcNow - when < _recheckAfter)
                            {
                                map[id] = when;
                            }
                        }
                    }

                    if (lines > map.Count + 500)
                    {
                        File.WriteAllLines(_checkedPath, map.Select(e => string.Create(CultureInfo.InvariantCulture, $"{e.Key:N}\t{e.Value.Ticks}")));
                    }
                }
                else
                {
                    Directory.CreateDirectory(cachePath);
                }
            }
            catch (IOException)
            {
                // Start empty
            }

            _checked = map;
            return map;
        }
    }

    private sealed class EndScope : IDisposable
    {
        public void Dispose() => _active.Value = false;
    }
}
