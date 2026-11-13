using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Khylo.Controller.Library;

/// <summary>
/// Whether the storage behind each library can be reached. Media often lives on another computer or a NAS,
/// and when that reboots, a mapped drive can stay disconnected until something reconnects it. Khylo checks
/// regularly, reconnects what it can (and right before playback), and shows the admin what can't be reached.
/// </summary>
public static class StorageHealth
{
    private static readonly ConcurrentDictionary<string, StorageLocationStatus> _locations = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string> _shareProblems = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets how to reconnect a path's storage (set by the server; on Windows it restores mapped drives).
    /// </summary>
    public static Func<string, bool>? Reconnect { get; set; }

    /// <summary>
    /// Gets what's known about each library location.
    /// </summary>
    /// <returns>The locations.</returns>
    public static IReadOnlyList<StorageLocationStatus> Snapshot()
    {
        var all = _locations.Values.OrderBy(l => l.Library, StringComparer.OrdinalIgnoreCase).ThenBy(l => l.Path, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var location in all)
        {
            location.Reason = location.Reachable ? null : ReasonFor(location.Path);
        }

        return all;
    }

    /// <summary>
    /// Remembers why a network share couldn't be connected (the manager clears it once it connects).
    /// </summary>
    /// <param name="share">The share, for example \nas\Movies.</param>
    /// <param name="problem">What went wrong, in plain words, or null once it works.</param>
    public static void SetShareProblem(string share, string? problem)
    {
        var key = NetworkNames.WithoutDot(share).TrimEnd('\\');
        if (problem is null)
        {
            _shareProblems.TryRemove(key, out _);
        }
        else
        {
            _shareProblems[key] = problem;
        }
    }

    private static string? ReasonFor(string path)
    {
        var plain = NetworkNames.WithoutDot(path);
        foreach (var (share, problem) in _shareProblems)
        {
            if (plain.StartsWith(share, StringComparison.OrdinalIgnoreCase)
                && (plain.Length == share.Length || plain[share.Length] == '\\'))
            {
                return problem;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks a folder or file can be reached, giving up after a few seconds (a dropped network share can
    /// otherwise hang for a long time).
    /// </summary>
    /// <param name="path">The folder or file.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <returns>Whether it answered.</returns>
    public static bool IsReachable(string path, TimeSpan timeout)
    {
        var check = Task.Run(() =>
        {
            try
            {
                if (File.Exists(path))
                {
                    return true;
                }

                // A folder counts when it can actually be listed, not just when its name is known
                return Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Take(1).Count() >= 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        });

        return check.Wait(timeout) && check.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Makes sure a media file's storage is reachable before playing it, reconnecting it first if it dropped.
    /// </summary>
    /// <param name="path">The media file.</param>
    /// <returns>Whether it can be reached.</returns>
    public static bool EnsureReachable(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Contains("://", StringComparison.Ordinal))
        {
            return true;
        }

        if (IsReachable(path, TimeSpan.FromSeconds(4)))
        {
            return true;
        }

        return Reconnect?.Invoke(path) == true && IsReachable(path, TimeSpan.FromSeconds(6));
    }

    /// <summary>
    /// Records the latest check of a library location.
    /// </summary>
    /// <param name="library">The library's name.</param>
    /// <param name="path">The location.</param>
    /// <param name="reachable">Whether it could be reached.</param>
    /// <returns>Whether that's a change from before (it went down or came back).</returns>
    public static bool Record(string library, string path, bool reachable)
    {
        var now = DateTime.UtcNow;
        var changed = false;
        _locations.AddOrUpdate(
            path,
            _ =>
            {
                changed = !reachable;
                return new StorageLocationStatus { Library = library, Path = path, Reachable = reachable, Since = now, LastChecked = now };
            },
            (_, old) =>
            {
                changed = old.Reachable != reachable;
                return new StorageLocationStatus { Library = library, Path = path, Reachable = reachable, Since = changed ? now : old.Since, LastChecked = now };
            });
        return changed;
    }

    /// <summary>
    /// Forgets locations that are no longer part of any library.
    /// </summary>
    /// <param name="current">The locations libraries have now.</param>
    public static void Keep(IEnumerable<string> current)
    {
        var keep = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
        foreach (var path in _locations.Keys.Where(p => !keep.Contains(p)).ToList())
        {
            _locations.TryRemove(path, out _);
        }
    }
}
