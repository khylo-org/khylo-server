using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Sockets;

namespace Khylo.Providers.Manager;

/// <summary>
/// Remembers online metadata sources that can't be reached right now (blocked by a firewall, down, or
/// no internet), so a library refresh skips them for a while and moves straight on to the next source
/// instead of waiting for every single lookup to time out.
/// </summary>
public static class RemoteSourceHealth
{
    private static readonly TimeSpan _skipFor = TimeSpan.FromMinutes(30);

    private static readonly ConcurrentDictionary<string, DateTime> _downUntil = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a source failed to connect recently and should be skipped for now.
    /// </summary>
    /// <param name="name">The source's name.</param>
    /// <returns>Whether to skip it.</returns>
    public static bool IsDown(string name)
    {
        if (_downUntil.TryGetValue(name, out var until))
        {
            if (DateTime.UtcNow < until)
            {
                return true;
            }

            _downUntil.TryRemove(name, out _);
        }

        return false;
    }

    /// <summary>
    /// Looks at a source's error and, if it means the source couldn't be reached at all, skips it for a while.
    /// </summary>
    /// <param name="name">The source's name.</param>
    /// <param name="error">The error it failed with.</param>
    /// <returns>Whether the source is now being skipped.</returns>
    public static bool Report(string name, Exception error)
    {
        if (!IsConnectionFailure(error))
        {
            return false;
        }

        _downUntil[name] = DateTime.UtcNow + _skipFor;
        return true;
    }

    private static bool IsConnectionFailure(Exception? error)
    {
        for (var e = error; e is not null; e = e.InnerException)
        {
            if (e is SocketException)
            {
                return true;
            }

            if (e is HttpRequestException http
                && http.HttpRequestError is HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError or HttpRequestError.SecureConnectionError)
            {
                return true;
            }
        }

        return false;
    }
}
