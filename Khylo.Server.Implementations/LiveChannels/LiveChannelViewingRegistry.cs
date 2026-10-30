using System;
using System.Collections.Concurrent;
using Khylo.Controller.LiveChannels;

namespace Khylo.Server.Implementations.LiveChannels;

/// <summary>
/// In-memory record of which items each device is playing through a live channel.
/// </summary>
public class LiveChannelViewingRegistry : ILiveChannelViewingRegistry
{
    // Time left for the player's final stop report after a device leaves the channels
    private static readonly TimeSpan _releaseGrace = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<(Guid UserId, string DeviceId, Guid ItemId), DateTime> _entries = new();

    /// <inheritdoc/>
    public void Register(Guid userId, string deviceId, Guid itemId, DateTime untilUtc)
    {
        ArgumentException.ThrowIfNullOrEmpty(deviceId);
        _entries.AddOrUpdate((userId, deviceId, itemId), untilUtc, (_, existing) => existing > untilUtc ? existing : untilUtc);
        RemoveExpired();
    }

    /// <inheritdoc/>
    public void Release(Guid userId, string deviceId)
    {
        ArgumentException.ThrowIfNullOrEmpty(deviceId);
        var releaseAt = DateTime.UtcNow.Add(_releaseGrace);
        foreach (var key in _entries.Keys)
        {
            if (key.UserId.Equals(userId) && string.Equals(key.DeviceId, deviceId, StringComparison.Ordinal))
            {
                _entries.AddOrUpdate(key, releaseAt, (_, existing) => existing < releaseAt ? existing : releaseAt);
            }
        }
    }

    /// <inheritdoc/>
    public bool IsChannelPlayback(Guid userId, string? deviceId, Guid itemId)
    {
        if (string.IsNullOrEmpty(deviceId))
        {
            return false;
        }

        return _entries.TryGetValue((userId, deviceId, itemId), out var untilUtc) && untilUtc > DateTime.UtcNow;
    }

    private void RemoveExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in _entries)
        {
            if (entry.Value <= now)
            {
                _entries.TryRemove(entry.Key, out _);
            }
        }
    }
}
