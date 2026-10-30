using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Database.Implementations.Entities;
using Khylo.Model.LiveChannels;

namespace Khylo.Controller.LiveChannels;

/// <summary>
/// Khylo live channels: always-on "TV stations" built from the library. A channel is only a
/// deterministic schedule; nothing plays or transcodes until a viewer tunes in, and tuning in plays
/// the scheduled library item from the scheduled position.
/// </summary>
public interface ILiveChannelManager
{
    /// <summary>
    /// Gets a value indicating whether Live Channels is turned on. While it is off, no channel is
    /// shown, tuned or scheduled.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Turns Live Channels on or off for the whole server.
    /// </summary>
    /// <param name="enabled">Whether to turn it on.</param>
    void SetEnabled(bool enabled);

    /// <summary>
    /// Turns channels on or off. Built-in, network and collection channels remember this by key.
    /// </summary>
    /// <param name="channelIds">The channels.</param>
    /// <param name="enabled">Whether to turn them on.</param>
    void SetChannelsEnabled(IReadOnlyCollection<Guid> channelIds, bool enabled);

    /// <summary>
    /// Gets the master switch and every channel with where it stands, for administrators. Other
    /// people's personal collection channels are left out.
    /// </summary>
    /// <param name="user">The administrator.</param>
    /// <returns>The admin view.</returns>
    LiveChannelAdminDto GetAdminStatus(User user);

    /// <summary>
    /// Gets the channel definitions, including disabled ones.
    /// </summary>
    /// <returns>The channel definitions.</returns>
    IReadOnlyList<LiveChannelDefinition> GetDefinitions();

    /// <summary>
    /// Gets the channels a user can watch with their programs between two times.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="startUtc">The window start.</param>
    /// <param name="endUtc">The window end.</param>
    /// <returns>The channels, ordered by number.</returns>
    IReadOnlyList<LiveChannelDto> GetGuide(User user, DateTime startUtc, DateTime endUtc);

    /// <summary>
    /// Tunes in to a channel: the program on now, how far into it, and what follows.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="deviceId">The device tuning in, so its playback does not change watch progress.</param>
    /// <param name="channelId">The channel id.</param>
    /// <returns>The tune result, or null if the channel does not exist or the user cannot watch it.</returns>
    LiveChannelTuneResult? Tune(User user, string? deviceId, Guid channelId);

    /// <summary>
    /// Gets a channel's details: its schedule from now until a time, and the titles it rotates through.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="channelId">The channel id.</param>
    /// <param name="endUtc">How far ahead to include the schedule.</param>
    /// <returns>The details, or null if the channel does not exist or the user cannot watch it.</returns>
    LiveChannelDetailsDto? GetChannelDetails(User user, Guid channelId, DateTime endUtc);

    /// <summary>
    /// Gets the local file of a channel's logo (network channels), fetching it the first time.
    /// </summary>
    /// <param name="channelId">The channel id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The logo file path, or null if the channel has no logo.</returns>
    Task<string?> GetChannelLogoPathAsync(Guid channelId, CancellationToken cancellationToken);

    /// <summary>
    /// Called when a device stops watching channels.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="deviceId">The device.</param>
    void Leave(User user, string? deviceId);

    /// <summary>
    /// Discards all schedules and rebuilds them from the current library.
    /// </summary>
    void ResetSchedules();
}
