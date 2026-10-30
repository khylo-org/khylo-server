using System;

namespace Khylo.Controller.LiveChannels;

/// <summary>
/// Tracks which items a device is playing as part of a live channel, so that playback does not
/// change the user's watch progress (resume position, played state, Continue Watching, Next Up).
/// </summary>
public interface ILiveChannelViewingRegistry
{
    /// <summary>
    /// Marks an item as channel playback for a user's device until a given time.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="deviceId">The device id.</param>
    /// <param name="itemId">The item id.</param>
    /// <param name="untilUtc">When the mark expires.</param>
    void Register(Guid userId, string deviceId, Guid itemId, DateTime untilUtc);

    /// <summary>
    /// Ends channel playback marks for a device shortly, leaving time for its final stop report.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="deviceId">The device id.</param>
    void Release(Guid userId, string deviceId);

    /// <summary>
    /// Gets a value indicating whether playback of an item on a device is channel playback.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="deviceId">The device id.</param>
    /// <param name="itemId">The item id.</param>
    /// <returns>True if the playback comes from a live channel.</returns>
    bool IsChannelPlayback(Guid userId, string? deviceId, Guid itemId);
}
