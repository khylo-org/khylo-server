using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Khylo.Server.Core.Updates;

/// <summary>
/// Reading Khylo's public release page: which build is newest on a channel, whether it's newer than the running
/// one, and the checksum that goes with a download. Pure logic, no network.
/// </summary>
public static class KhyloReleaseFeed
{
    /// <summary>The only place downloads are accepted from.</summary>
    public const string DownloadPrefix = "https://github.com/khylo-org/khylo/releases/download/";

    /// <summary>
    /// The release's checksum list.
    /// </summary>
    public const string ChecksumsAsset = "SHA256SUMS.txt";

    /// <summary>
    /// The signature of the checksum list, made with Khylo's private key when the release is published.
    /// </summary>
    public const string SignatureAsset = "SHA256SUMS.txt.sig";

    private static readonly System.Text.RegularExpressions.Regex _tagPattern = new(
        @"^(DEV-)?V[0-9]{2}\.[0-9]{2}\.[0-9]{2,3}(\.[0-9]{2})?\z",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Says whether a release name is a real Khylo build number (DEV-V26.11.08.11, V26.11.08.11 or the older DEV-V26.10.101).
    /// Nothing else is allowed into file names or the update script.
    /// </summary>
    /// <param name="tag">The release name.</param>
    /// <returns>True for a build number.</returns>
    public static bool IsValidTag(string? tag) => tag is not null && _tagPattern.IsMatch(tag);

    /// <summary>
    /// Reads the release list.
    /// </summary>
    /// <param name="json">GitHub's answer for the releases of a repository.</param>
    /// <param name="channel">"stable" takes only finished releases; "dev" takes every build.</param>
    /// <returns>The releases on that channel, newest first.</returns>
    public static IReadOnlyList<Release> Parse(string json, string channel)
    {
        var releases = new List<Release>();
        using var doc = JsonDocument.Parse(json);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("draft", out var draft) && draft.GetBoolean())
            {
                continue;
            }

            var tag = item.GetProperty("tag_name").GetString() ?? string.Empty;
            var preRelease = item.TryGetProperty("prerelease", out var pre) && pre.GetBoolean();
            var isDevBuild = tag.StartsWith("DEV-", StringComparison.OrdinalIgnoreCase);
            if (IsStable(channel) && (isDevBuild || preRelease))
            {
                continue;
            }

            var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (item.TryGetProperty("assets", out var list))
            {
                foreach (var asset in list.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString();
                    var url = asset.GetProperty("browser_download_url").GetString();
                    if (name is not null && url is not null)
                    {
                        assets[name] = url;
                    }
                }
            }

            DateTime? published = item.TryGetProperty("published_at", out var at) && at.ValueKind == JsonValueKind.String
                && DateTime.TryParse(at.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
                    ? parsed
                    : null;
            releases.Add(new Release(
                tag,
                item.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty,
                item.TryGetProperty("html_url", out var html) ? html.GetString() ?? string.Empty : string.Empty,
                published,
                assets));
        }

        return releases.OrderByDescending(r => Key(r.Tag), VersionComparer.Instance).ToList();
    }

    /// <summary>
    /// Whether a build is newer than another.
    /// </summary>
    /// <param name="candidate">The build on offer.</param>
    /// <param name="current">The running build.</param>
    /// <returns>True when the candidate is newer.</returns>
    public static bool IsNewer(string candidate, string current)
        => VersionComparer.Instance.Compare(Key(candidate), Key(current)) > 0;

    /// <summary>
    /// Turns a build name into its numbers: DEV-V26.11.04.02 becomes 26, 11, 4, 2.
    /// </summary>
    /// <param name="build">The build name.</param>
    /// <returns>The numbers in it.</returns>
    public static int[] Key(string build)
        => System.Text.RegularExpressions.Regex.Matches(build ?? string.Empty, "[0-9]+")
            .Select(m => int.TryParse(m.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0)
            .ToArray();

    /// <summary>
    /// Takes just the "What's new in this build" part out of a release's description (the rest is download buttons and
    /// notes) and turns it into plain text, one line per item.
    /// </summary>
    /// <param name="body">The release description.</param>
    /// <returns>The plain text, or nothing if the description has no such part.</returns>
    public static string WhatsNew(string body)
    {
        var lines = (body ?? string.Empty).Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
        var start = Array.FindIndex(lines, l => l.StartsWith("## What's new", StringComparison.OrdinalIgnoreCase));
        if (start < 0)
        {
            return string.Empty;
        }

        var result = new List<string>();
        for (var i = start + 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("Full list:", StringComparison.OrdinalIgnoreCase) || line.StartsWith("## ", StringComparison.Ordinal))
            {
                break;
            }

            if (line.Length > 0)
            {
                result.Add(Plain(line));
            }
        }

        return string.Join('\n', result);
    }

    /// <summary>
    /// Finds the checksum of a file in a SHA256SUMS list ("hash  file" per line).
    /// </summary>
    /// <param name="sums">The list.</param>
    /// <param name="fileName">The file.</param>
    /// <returns>The checksum in lower case, or null if the file isn't listed.</returns>
    public static string? ChecksumFor(string sums, string fileName)
    {
        foreach (var line in sums.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && string.Equals(parts[1].TrimStart('*').Trim(), fileName, StringComparison.OrdinalIgnoreCase))
            {
                return parts[0].ToLowerInvariant();
            }
        }

        return null;
    }

    /// <summary>
    /// Gets whether a channel name means finished releases only.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <returns>True for stable (anything that isn't "dev").</returns>
    public static bool IsStable(string channel) => !string.Equals(channel, "dev", StringComparison.OrdinalIgnoreCase);

    private static string Plain(string line)
    {
        var noBold = System.Text.RegularExpressions.Regex.Replace(line, @"\*\*(.+?)\*\*", "$1");
        return System.Text.RegularExpressions.Regex.Replace(noBold, @"\[([^\]]+)\]\([^)]+\)", "$1");
    }

    /// <summary>
    /// A release on the page.
    /// </summary>
    /// <param name="Tag">The build, for example DEV-V26.11.04.02.</param>
    /// <param name="Notes">What's new.</param>
    /// <param name="Url">The release's page.</param>
    /// <param name="PublishedAt">When it was published.</param>
    /// <param name="Assets">File name to download address.</param>
    public sealed record Release(string Tag, string Notes, string Url, DateTime? PublishedAt, IReadOnlyDictionary<string, string> Assets);

    private sealed class VersionComparer : IComparer<int[]>
    {
        public static readonly VersionComparer Instance = new();

        public int Compare(int[]? x, int[]? y)
        {
            x ??= [];
            y ??= [];
            for (var i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                var a = i < x.Length ? x[i] : 0;
                var b = i < y.Length ? y[i] : 0;
                if (a != b)
                {
                    return a.CompareTo(b);
                }
            }

            return 0;
        }
    }
}
