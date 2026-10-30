using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Api.Extensions;
using Khylo.Controller.Session;
using Khylo.Model.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Khylo.Api.Controllers;

/// <summary>
/// Handoff: move what you are watching or listening to from one of your devices to another, right where you
/// were. Works both ways: send it from the device that is playing, or pull it to the device in your hand.
/// </summary>
/// <remarks>
/// Only the signed-in person's own devices can be moved between. The new device is told to play first; the old
/// one stops only after that, so a failed handoff never cuts playback off.
/// </remarks>
[Route("Khylo/Handoff")]
[Authorize]
public class KhyloHandoffController : BaseKhyloApiController
{
    private readonly ISessionManager _sessionManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="KhyloHandoffController"/> class.
    /// </summary>
    /// <param name="sessionManager">The session manager.</param>
    public KhyloHandoffController(ISessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    /// <summary>
    /// Lists the signed-in person's other devices that can take part in a handoff.
    /// </summary>
    /// <response code="200">The devices.</response>
    /// <returns>Other devices of this person that are online and can be controlled, and what each is playing.</returns>
    [HttpGet("Devices")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<HandoffDevice>> GetDevices()
    {
        var userId = User.GetUserId();
        var thisDevice = User.GetDeviceId();

        var devices = _sessionManager.Sessions
            .Where(s => s.UserId.Equals(userId)
                && s.IsActive
                && s.SupportsRemoteControl
                && !string.Equals(s.DeviceId, thisDevice, StringComparison.Ordinal))
            .Select(s => new HandoffDevice
            {
                SessionId = s.Id,
                DeviceName = s.DeviceName,
                Client = s.Client,
                DeviceType = s.DeviceType,
                IsPlaying = s.NowPlayingItem is not null,
                NowPlayingItemId = s.NowPlayingItem?.Id,
                NowPlayingName = s.NowPlayingItem?.Name,
                NowPlayingSeriesName = s.NowPlayingItem?.SeriesName,
                PositionTicks = s.PlayState?.PositionTicks,
                RunTimeTicks = s.NowPlayingItem?.RunTimeTicks
            })
            .OrderByDescending(d => d.IsPlaying)
            .ThenBy(d => d.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return devices;
    }

    /// <summary>
    /// Moves what one device is playing to another, continuing from the same moment.
    /// </summary>
    /// <param name="request">Which device to take it from and which to give it to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">The other device was told to continue, and this one stopped.</response>
    /// <response code="400">Nothing is playing on the first device.</response>
    /// <response code="403">One of the devices isn't yours.</response>
    /// <response code="404">A device is no longer online.</response>
    /// <returns>No content.</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Handoff([FromBody] HandoffRequest request, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        var sessions = _sessionManager.Sessions.ToList();
        var thisDevice = User.GetDeviceId();

        // Leaving a side empty means "this device", so an app doesn't need to look up its own session
        var from = string.IsNullOrEmpty(request.FromSessionId)
            ? sessions.FirstOrDefault(s => s.UserId.Equals(userId) && string.Equals(s.DeviceId, thisDevice, StringComparison.Ordinal))
            : sessions.FirstOrDefault(s => string.Equals(s.Id, request.FromSessionId, StringComparison.Ordinal));
        var to = string.IsNullOrEmpty(request.ToSessionId)
            ? sessions.FirstOrDefault(s => s.UserId.Equals(userId) && string.Equals(s.DeviceId, thisDevice, StringComparison.Ordinal))
            : sessions.FirstOrDefault(s => string.Equals(s.Id, request.ToSessionId, StringComparison.Ordinal));
        if (from is null || to is null || !to.IsActive || !to.SupportsRemoteControl)
        {
            return NotFound("That device isn't online any more.");
        }

        if (!from.UserId.Equals(userId) || !to.UserId.Equals(userId))
        {
            return Forbid();
        }

        if (string.Equals(from.Id, to.Id, StringComparison.Ordinal) || from.NowPlayingItem is null)
        {
            return BadRequest("Nothing is playing on that device.");
        }

        var current = from.NowPlayingItem.Id;
        var ids = new List<Guid> { current };
        var queue = from.NowPlayingQueue;
        if (queue is { Count: > 1 })
        {
            var at = queue.ToList().FindIndex(q => q.Id.Equals(current));
            if (at >= 0)
            {
                ids = queue.Skip(at).Select(q => q.Id).ToList();
            }
        }

        var state = from.PlayState;

        // The device that is playing knows its exact moment better than its last progress report (sent every few
        // seconds), so when it is the one asking it may say where it is
        var fromThisDevice = string.Equals(from.DeviceId, thisDevice, StringComparison.Ordinal);
        var position = fromThisDevice && request.PositionTicks is not null ? request.PositionTicks : state?.PositionTicks;
        var audio = fromThisDevice && request.AudioStreamIndex is not null ? request.AudioStreamIndex : state?.AudioStreamIndex;
        var subtitle = fromThisDevice && request.SubtitleStreamIndex is not null ? request.SubtitleStreamIndex : state?.SubtitleStreamIndex;
        var controller = sessions.FirstOrDefault(s => s.UserId.Equals(userId) && string.Equals(s.DeviceId, thisDevice, StringComparison.Ordinal))?.Id ?? from.Id;

        await _sessionManager.SendPlayCommand(
            controller,
            to.Id,
            new PlayRequest
            {
                ItemIds = ids.ToArray(),
                PlayCommand = PlayCommand.PlayNow,
                StartPositionTicks = position,
                MediaSourceId = state?.MediaSourceId,
                AudioStreamIndex = audio,
                SubtitleStreamIndex = subtitle,
                ControllingUserId = userId
            },
            cancellationToken).ConfigureAwait(false);

        // Only now that the other device has its orders does this one let go
        await _sessionManager.SendPlaystateCommand(
            controller,
            from.Id,
            new PlaystateRequest { Command = PlaystateCommand.Stop, ControllingUserId = userId.ToString("N", System.Globalization.CultureInfo.InvariantCulture) },
            cancellationToken).ConfigureAwait(false);

        return NoContent();
    }
}
