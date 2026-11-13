using System;

namespace Khylo.Controller.Library;

/// <summary>
/// The two spellings of one file server: \\nas\Movies and \\nas.\Movies (a dot after the server's name). They reach the same
/// computer, but Windows signs in to them differently. The plain name uses the domain's tickets (Kerberos). The dotted name has
/// no ticket, so Windows falls back to the older sign-in (NTLM), which only works while the file server can reach its domain
/// controller. After the file server restarts that can be down for a while, and a library written with the dot goes
/// unreachable while the same share under its plain name keeps working.
/// </summary>
public static class NetworkNames
{
    /// <summary>
    /// Says whether a network path names its server with a dot after it (\\nas.\Movies).
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>True for a network path written that way.</returns>
    public static bool HasDottedServer(string? path)
    {
        var host = ServerOf(path);
        return host is { Length: > 1 } && host[^1] == '.';
    }

    /// <summary>
    /// Gives a network path without the dot after the server's name. Other paths are returned as they are.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The path, \\nas.\Movies becoming \\nas\Movies.</returns>
    public static string WithoutDot(string path)
    {
        if (!HasDottedServer(path))
        {
            return path;
        }

        var host = ServerOf(path)!;
        return string.Concat(@"\\", host.AsSpan(0, host.Length - 1), path.AsSpan(2 + host.Length));
    }

    /// <summary>
    /// Gives a server's name without a dot after it.
    /// </summary>
    /// <param name="host">The server's name or address.</param>
    /// <returns>The name, nas. becoming nas.</returns>
    public static string HostWithoutDot(string host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return host.Length > 1 ? host.TrimEnd('.') : host;
    }

    /// <summary>
    /// Says whether two network paths are the same place, whichever way the server is spelled.
    /// </summary>
    /// <param name="a">One path.</param>
    /// <param name="b">The other.</param>
    /// <returns>True when they only differ in the dot after the server's name (or in case, or a closing slash).</returns>
    public static bool SameShare(string? a, string? b)
        => string.Equals(
            WithoutDot(a ?? string.Empty).TrimEnd('\\'),
            WithoutDot(b ?? string.Empty).TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Says what is wrong with a path written with a dot after the server's name, in plain words.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The advice, or null when the path is fine.</returns>
    public static string? Warning(string? path)
        => path is not null && HasDottedServer(path)
            ? $"{path} is written with a dot after the server's name. Windows signs in to that spelling the older way, which can stop working after the file server restarts. {WithoutDot(path)} reaches the same place without that problem."
            : null;

    private static string? ServerOf(string? path)
    {
        if (path is null || path.Length < 3 || !path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return null;
        }

        var end = path.IndexOf('\\', 2);
        return end < 0 ? path[2..] : path[2..end];
    }
}
