using System;
using System.Collections.Generic;
using System.Linq;

namespace Khylo.Api.Helpers;

/// <summary>
/// Slows down password guessing. After too many wrong passwords for one account, or from one address, sign-in
/// pauses for a while for that account or address. Nothing is disabled and nobody signed in is affected; the
/// right password works again once the pause ends.
/// </summary>
public static class LoginThrottle
{
    private const int MaxFailuresPerAccount = 10;
    private const int MaxFailuresPerAddress = 30;
    private static readonly TimeSpan _window = TimeSpan.FromMinutes(15);

    private static readonly object _lock = new();
    private static readonly Dictionary<string, List<DateTime>> _failures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// How long until this account or address may try again, or null if it may now.
    /// </summary>
    /// <param name="account">The user name (or id) being signed in to.</param>
    /// <param name="address">The address the attempt comes from.</param>
    /// <returns>The wait, or null.</returns>
    public static TimeSpan? RetryAfter(string? account, string address)
    {
        var now = DateTime.UtcNow;
        lock (_lock)
        {
            var waits = new[] { Wait("u:" + account, MaxFailuresPerAccount, now), Wait("ip:" + address, MaxFailuresPerAddress, now) };
            return waits.Where(w => w.HasValue).Select(w => w!.Value).DefaultIfEmpty().Max() is var longest && longest > TimeSpan.Zero ? longest : null;
        }
    }

    /// <summary>
    /// Notes a wrong password.
    /// </summary>
    /// <param name="account">The user name (or id).</param>
    /// <param name="address">The address it came from.</param>
    public static void RecordFailure(string? account, string address)
    {
        var now = DateTime.UtcNow;
        lock (_lock)
        {
            Add("u:" + account, now);
            Add("ip:" + address, now);
            Prune(now);
        }
    }

    /// <summary>
    /// A correct sign-in clears the account's count.
    /// </summary>
    /// <param name="account">The user name (or id).</param>
    public static void RecordSuccess(string? account)
    {
        lock (_lock)
        {
            _failures.Remove("u:" + account);
        }
    }

    private static TimeSpan? Wait(string key, int max, DateTime now)
    {
        if (!_failures.TryGetValue(key, out var times))
        {
            return null;
        }

        var recent = times.Where(t => t > now - _window).OrderBy(t => t).ToList();
        if (recent.Count < max)
        {
            return null;
        }

        // Free again once the oldest of the last `max` failures leaves the window
        return recent[recent.Count - max] + _window - now;
    }

    private static void Add(string key, DateTime now)
    {
        if (!_failures.TryGetValue(key, out var times))
        {
            _failures[key] = times = [];
        }

        times.Add(now);
    }

    private static void Prune(DateTime now)
    {
        foreach (var key in _failures.Keys.ToList())
        {
            _failures[key].RemoveAll(t => t <= now - _window);
            if (_failures[key].Count == 0)
            {
                _failures.Remove(key);
            }
        }
    }
}
