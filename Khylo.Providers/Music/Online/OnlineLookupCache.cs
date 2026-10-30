using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using Khylo.Common.Configuration;

namespace Khylo.Providers.Music.Online;

/// <summary>
/// Remembers which online lookups were already done (found or not found), so library scans don't ask
/// the same source about the same artist again and again. What was found is already saved on the item
/// itself, so only one short line per lookup is kept here: a few hundred KB even for huge libraries.
/// Found results are rechecked after 6 months, misses after a month (new artists get added online).
/// </summary>
public static class OnlineLookupCache
{
    private static readonly TimeSpan _keepFound = TimeSpan.FromDays(180);
    private static readonly TimeSpan _keepMissing = TimeSpan.FromDays(30);
    private static readonly object _fileLock = new();
    private static ConcurrentDictionary<string, (bool Found, DateTime When)>? _entries;
    private static string? _path;

    /// <summary>
    /// Whether this lookup was done recently enough to skip.
    /// </summary>
    /// <param name="paths">The application paths.</param>
    /// <param name="source">The online source, for example "wikipedia".</param>
    /// <param name="name">What was looked up (an artist's name).</param>
    /// <returns>True when it can be skipped.</returns>
    public static bool WasLookedUp(IApplicationPaths paths, string source, string? name)
    {
        var entries = Load(paths);
        if (!entries.TryGetValue(Key(source, name), out var entry))
        {
            return false;
        }

        return DateTime.UtcNow - entry.When < (entry.Found ? _keepFound : _keepMissing);
    }

    /// <summary>
    /// Records that a lookup was done.
    /// </summary>
    /// <param name="paths">The application paths.</param>
    /// <param name="source">The online source.</param>
    /// <param name="name">What was looked up.</param>
    /// <param name="found">Whether anything was found.</param>
    public static void Record(IApplicationPaths paths, string source, string? name, bool found)
    {
        var entries = Load(paths);
        var key = Key(source, name);
        var now = DateTime.UtcNow;
        entries[key] = (found, now);
        lock (_fileLock)
        {
            try
            {
                File.AppendAllText(_path!, string.Create(CultureInfo.InvariantCulture, $"{key}\t{(found ? 1 : 0)}\t{now.Ticks}\n"));
            }
            catch (IOException)
            {
                // The cache only saves time; losing a line just means asking again later
            }
        }
    }

    private static string Key(string source, string? name) => source + "|" + MusicNameMatch.Normalize(name);

    private static ConcurrentDictionary<string, (bool Found, DateTime When)> Load(IApplicationPaths paths)
    {
        if (_entries is not null)
        {
            return _entries;
        }

        lock (_fileLock)
        {
            if (_entries is not null)
            {
                return _entries;
            }

            _path = Path.Combine(paths.CachePath, "khylo-online-lookups.tsv");
            var entries = new ConcurrentDictionary<string, (bool, DateTime)>(StringComparer.Ordinal);
            var lines = 0;
            try
            {
                if (File.Exists(_path))
                {
                    foreach (var line in File.ReadLines(_path))
                    {
                        lines++;
                        var parts = line.Split('\t');
                        if (parts.Length == 3 && long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks))
                        {
                            var found = parts[1] == "1";
                            var when = new DateTime(ticks, DateTimeKind.Utc);
                            if (DateTime.UtcNow - when < (found ? _keepFound : _keepMissing))
                            {
                                entries[parts[0]] = (found, when);
                            }
                        }
                    }

                    // Rewrite without old and repeated lines so the file stays small
                    if (lines > entries.Count + 500)
                    {
                        File.WriteAllLines(_path, entries.Select(e => string.Create(CultureInfo.InvariantCulture, $"{e.Key}\t{(e.Value.Item1 ? 1 : 0)}\t{e.Value.Item2.Ticks}")));
                    }
                }
                else
                {
                    Directory.CreateDirectory(paths.CachePath);
                }
            }
            catch (IOException)
            {
                // Start empty
            }

            _entries = entries;
            return entries;
        }
    }
}
