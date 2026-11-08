using System;
using System.Collections.Generic;

namespace Khylo.Controller.Mirroring;

/// <summary>
/// Mirroring: one of a person's devices plays, and their other devices follow it. The followers start what it plays, pause when
/// it pauses, and are nudged back when they drift away from it.
/// </summary>
public interface IMirrorService
{
    /// <summary>
    /// Lists a person's running mirrors.
    /// </summary>
    /// <param name="userId">The person.</param>
    /// <returns>The mirrors.</returns>
    IReadOnlyList<MirrorInfo> List(Guid userId);

    /// <summary>
    /// Starts a mirror, or adds followers to a running one. If the followed device is already playing, the followers join in at once.
    /// </summary>
    /// <param name="userId">The person; every device must be theirs.</param>
    /// <param name="sourceSessionId">The device to follow.</param>
    /// <param name="targetSessionIds">The devices that follow it.</param>
    /// <returns>The mirror.</returns>
    /// <exception cref="InvalidOperationException">A device is offline, isn't the person's, or can't be controlled; the message says which.</exception>
    MirrorInfo Start(Guid userId, string sourceSessionId, IReadOnlyCollection<string> targetSessionIds);

    /// <summary>
    /// Says where the device being followed is, for a follower that keeps itself in step by adjusting its own speed. A follower that
    /// asks regularly is left to correct itself, so the server does not also jump it around.
    /// </summary>
    /// <param name="userId">The person.</param>
    /// <param name="deviceId">The follower's device id.</param>
    /// <returns>The reference, or <c>null</c> if that device is not following something that is playing.</returns>
    MirrorReference? ReferenceFor(Guid userId, string deviceId);

    /// <summary>
    /// Says which Khylo TV channel a device is watching. Khylo TV channels are not library items, so the device tells the server
    /// itself; followers are then sent to the same channel (a channel runs on the clock, so they land on the same moment).
    /// </summary>
    /// <param name="userId">The person.</param>
    /// <param name="deviceId">The device that is watching.</param>
    /// <param name="channelId">The channel, or <c>null</c> when the device has left it.</param>
    void SetChannel(Guid userId, string deviceId, string? channelId);

    /// <summary>
    /// Ends a mirror when given the followed device, or takes a device out of its mirror when given a follower.
    /// Devices that are playing carry on by themselves.
    /// </summary>
    /// <param name="userId">The person.</param>
    /// <param name="sessionId">The followed device or a follower.</param>
    /// <returns><c>true</c> if something was stopped.</returns>
    bool Stop(Guid userId, string sessionId);
}
