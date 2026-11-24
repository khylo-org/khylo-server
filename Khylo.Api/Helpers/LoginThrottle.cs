using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace Khylo.Api.Helpers;

/// <summary>
/// Slows down password and PIN guessing. Wrong guesses are counted for each account from each address, for each address,
/// and for each account across all addresses; when a count is full, sign-in pauses for a while. Nothing is disabled and
/// nobody signed in is affected; the right password works again once the pause ends.
/// <para>
/// The pause is built so that a stranger can't use it to keep the owner out. Wrong guesses from one address stop that
/// address only. The account-wide count (many addresses all guessing) only stops addresses that have never signed in to that
/// account, so the owner on a device they already use gets through. PINs are different: with only a few thousand possible,
/// their account-wide count stops everyone.
/// </para>
/// </summary>
public static class LoginThrottle
{
    private const int MaxFailuresPerAccountAndAddress = 10;
    private const int MaxFailuresPerAddress = 30;
    private const int MaxFailuresPerAccount = 100;
    private const int MaxFailuresPerPin = 10;
    private const int MaxKnownAddresses = 5000;
    private static readonly TimeSpan _window = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan _remember = TimeSpan.FromDays(30);

    private static readonly object _lock = new();
    private static readonly Dictionary<string, List<DateTime>> _failures = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, DateTime> _knownAddresses = new(StringComparer.OrdinalIgnoreCase);

    // Guesses that were let through but haven't finished yet. They count as failures straight away, so a burst of
    // simultaneous guesses can't all pass the check before the first one is recorded. Each one expires on its own in case
    // the request never reaches RecordFailure or RecordSuccess.
    private static readonly TimeSpan _pendingLife = TimeSpan.FromSeconds(60);
    private static readonly Dictionary<string, List<DateTime>> _pending = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// How long until this attempt may be made, or null if it may be made now.
    /// </summary>
    /// <param name="account">The user name (or a key for what is being guessed) being signed in to.</param>
    /// <param name="address">The address the attempt comes from.</param>
    /// <param name="pin">Whether a PIN is being guessed (few possibilities, so the account-wide count stops everyone).</param>
    /// <returns>The wait, or null.</returns>
    public static TimeSpan? RetryAfter(string? account, string address, bool pin = false)
    {
        var now = DateTime.UtcNow;
        var where = Normalize(address);
        lock (_lock)
        {
            var pairKey = PairKey(account, where);
            var waits = new List<TimeSpan?>
            {
                Wait(pairKey, MaxFailuresPerAccountAndAddress, now),
                Wait("ip:" + where, MaxFailuresPerAddress, now)
            };
            if (pin)
            {
                waits.Add(Wait("u:" + account, MaxFailuresPerPin, now));
            }
            else if (!IsKnown(account, where, now))
            {
                waits.Add(Wait("u:" + account, MaxFailuresPerAccount, now));
            }

            var longest = waits.Where(w => w.HasValue).Select(w => w!.Value).DefaultIfEmpty().Max();
            if (longest > TimeSpan.Zero)
            {
                return longest;
            }

            // Allowed: hold a place for this guess until it is recorded as right or wrong
            AddPending(pairKey, now);
            AddPending("ip:" + where, now);
            AddPending("u:" + account, now);
            return null;
        }
    }

    /// <summary>
    /// Gives back the place held by <see cref="RetryAfter"/> when the request ended without a right or wrong guess
    /// (an error, or something that isn't a guess), so it isn't counted against the person.
    /// </summary>
    /// <param name="account">The user name (or key) that was passed to <see cref="RetryAfter"/>.</param>
    /// <param name="address">The address it came from.</param>
    public static void Release(string? account, string address)
    {
        var where = Normalize(address);
        lock (_lock)
        {
            ReleasePending(PairKey(account, where));
            ReleasePending("ip:" + where);
            ReleasePending("u:" + account);
        }
    }

    /// <summary>
    /// Counts a wrong guess.
    /// </summary>
    /// <param name="account">The user name (or key) that was guessed at.</param>
    /// <param name="address">The address it came from.</param>
    /// <param name="accountExists">Whether that account is real. Guesses at names that don't exist only count against the address, so made-up names can't fill the memory.</param>
    public static void RecordFailure(string? account, string address, bool accountExists = true)
    {
        var now = DateTime.UtcNow;
        var where = Normalize(address);
        lock (_lock)
        {
            ReleasePending(PairKey(account, where));
            ReleasePending("ip:" + where);
            ReleasePending("u:" + account);
            Add("ip:" + where, now);
            if (accountExists)
            {
                Add(PairKey(account, where), now);
                Add("u:" + account, now);
            }

            Prune(now);
        }
    }

    /// <summary>
    /// Counts a right guess: this address stops being held for this account, and is remembered as one that signs in to it.
    /// </summary>
    /// <param name="account">The user name (or key).</param>
    /// <param name="address">The address it came from.</param>
    public static void RecordSuccess(string? account, string address)
    {
        var now = DateTime.UtcNow;
        var where = Normalize(address);
        lock (_lock)
        {
            ReleasePending(PairKey(account, where));
            ReleasePending("ip:" + where);
            ReleasePending("u:" + account);
            _failures.Remove(PairKey(account, where));
            _knownAddresses[PairKey(account, where)] = now;
            if (_knownAddresses.Count > MaxKnownAddresses)
            {
                foreach (var old in _knownAddresses.OrderBy(k => k.Value).Take(_knownAddresses.Count - (MaxKnownAddresses * 9 / 10)).Select(k => k.Key).ToList())
                {
                    _knownAddresses.Remove(old);
                }
            }
        }
    }

    // One address, and a whole /64 for IPv6, since a single machine has billions of addresses inside it
    private static string Normalize(string address)
    {
        if (!IPAddress.TryParse(address, out var ip))
        {
            return address;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (ip.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return ip.ToString();
        }

        var bytes = ip.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }

    private static string PairKey(string? account, string address) => "pair:" + account + "|" + address;

    private static bool IsKnown(string? account, string address, DateTime now)
        => _knownAddresses.TryGetValue(PairKey(account, address), out var last) && now - last < _remember;

    private static TimeSpan? Wait(string key, int max, DateTime now)
    {
        var recent = _failures.TryGetValue(key, out var times)
            ? times.Where(t => t > now - _window).OrderBy(t => t).ToList()
            : [];
        recent.AddRange(_pending.TryGetValue(key, out var held) ? held.Where(t => t > now - _pendingLife) : []);
        recent.Sort();
        if (recent.Count < max)
        {
            return null;
        }

        // Free again once the oldest of the last `max` failures leaves the window
        return recent[recent.Count - max] + _window - now;
    }

    private static void AddPending(string key, DateTime now)
    {
        if (!_pending.TryGetValue(key, out var held))
        {
            _pending[key] = held = [];
        }

        held.Add(now);
    }

    private static void ReleasePending(string key)
    {
        if (_pending.TryGetValue(key, out var held))
        {
            if (held.Count > 0)
            {
                held.RemoveAt(0);
            }

            if (held.Count == 0)
            {
                _pending.Remove(key);
            }
        }
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

        foreach (var key in _pending.Keys.ToList())
        {
            _pending[key].RemoveAll(t => t <= now - _pendingLife);
            if (_pending[key].Count == 0)
            {
                _pending.Remove(key);
            }
        }

        foreach (var key in _knownAddresses.Where(k => now - k.Value >= _remember).Select(k => k.Key).ToList())
        {
            _knownAddresses.Remove(key);
        }
    }
}
