using System;
using System.Collections.Generic;

namespace Khylo.LiveTv.Recordings;

/// <summary>
/// A network share can be written two ways: <c>\\server\share</c> and <c>\\server.\share</c> (with a dot after the server name).
/// Windows treats them as the same place, but comparing the text does not, so a folder saved one way was not found when
/// looked up the other way.
/// </summary>
public static class NetworkPath
{
    /// <summary>
    /// Gets the path written both ways, the original first.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The path, then the same path with the dot after the server name added or removed.</returns>
    public static IEnumerable<string> Variants(string path)
    {
        yield return path;
        var other = Toggle(path);
        if (!string.Equals(other, path, StringComparison.Ordinal))
        {
            yield return other;
        }
    }

    /// <summary>
    /// Writes a path in one fixed way, so two spellings of the same place compare equal.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The path with no dot after the server name.</returns>
    public static string Normalize(string path)
    {
        if (!path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return path.TrimEnd('\\');
        }

        var slash = path.IndexOf('\\', 2);
        var server = slash < 0 ? path[2..] : path[2..slash];
        var rest = slash < 0 ? string.Empty : path[slash..];
        return (@"\\" + server.TrimEnd('.') + rest).TrimEnd('\\');
    }

    private static string Toggle(string path)
    {
        if (!path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return path;
        }

        var slash = path.IndexOf('\\', 2);
        if (slash < 0)
        {
            return path;
        }

        var server = path[2..slash];
        return @"\\" + (server.EndsWith('.') ? server.TrimEnd('.') : server + ".") + path[slash..];
    }
}
