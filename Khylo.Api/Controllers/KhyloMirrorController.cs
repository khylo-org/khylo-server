using System;
using System.Collections.Generic;
using System.Linq;
using Khylo.Api.Extensions;
using Khylo.Controller.Mirroring;
using Khylo.Controller.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Khylo.Api.Controllers;

/// <summary>
/// Mirroring: play on one of your devices and have your other devices follow along, so the same thing is on every TV.
/// </summary>
/// <remarks>
/// Only the signed-in person's own devices can be mirrored. The devices to follow use the same device list as handoff
/// (<c>Khylo/Handoff/Devices</c>).
/// </remarks>
[Route("Khylo/Mirror")]
[Authorize]
public class KhyloMirrorController : BaseKhyloApiController
{
    private readonly IMirrorService _mirrors;
    private readonly ISessionManager _sessionManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="KhyloMirrorController"/> class.
    /// </summary>
    /// <param name="mirrors">The mirror service.</param>
    /// <param name="sessionManager">The session manager.</param>
    public KhyloMirrorController(IMirrorService mirrors, ISessionManager sessionManager)
    {
        _mirrors = mirrors;
        _sessionManager = sessionManager;
    }

    /// <summary>
    /// Lists the signed-in person's running mirrors.
    /// </summary>
    /// <response code="200">The mirrors.</response>
    /// <returns>The mirrors, each with the device being followed and the devices following it.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<MirrorInfo>> GetMirrors() => Ok(_mirrors.List(User.GetUserId()));

    /// <summary>
    /// Starts a mirror, or adds devices to a running one.
    /// </summary>
    /// <param name="request">The device to follow and the devices that follow it.</param>
    /// <response code="200">The mirror is running.</response>
    /// <response code="400">A device can't be mirrored; the message says why.</response>
    /// <returns>The mirror, with the device being followed and its followers.</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<MirrorInfo> Start([FromBody] MirrorRequest request)
    {
        var source = string.IsNullOrEmpty(request.SourceSessionId) ? ThisSession()?.Id : request.SourceSessionId;
        if (source is null)
        {
            return BadRequest("This device isn't online any more.");
        }

        try
        {
            return _mirrors.Start(User.GetUserId(), source, request.TargetSessionIds.ToList());
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Says where the device this one follows is right now, so this device can keep itself in step.
    /// </summary>
    /// <response code="200">Where the followed device is.</response>
    /// <response code="404">This device is not following something that is playing.</response>
    /// <returns>The followed device's item and position.</returns>
    [HttpGet("Reference")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<MirrorReference> GetReference()
    {
        var reference = _mirrors.ReferenceFor(User.GetUserId(), User.GetDeviceId() ?? string.Empty);
        return reference is null ? NotFound() : reference;
    }

    /// <summary>
    /// Says which Khylo TV channel this device is watching, so devices following it can tune to the same channel.
    /// </summary>
    /// <param name="request">The channel, or no channel when this device has left it.</param>
    /// <response code="204">Noted.</response>
    /// <returns>No content.</returns>
    [HttpPost("Channel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult SetChannel([FromBody] MirrorChannelRequest request)
    {
        _mirrors.SetChannel(User.GetUserId(), User.GetDeviceId() ?? string.Empty, request.ChannelId);
        return NoContent();
    }

    /// <summary>
    /// Ends a mirror, or takes a device out of one. Devices that are playing carry on.
    /// </summary>
    /// <param name="sessionId">The device being followed, or a device that follows. Leave out to mean the device asking.</param>
    /// <response code="204">It was stopped.</response>
    /// <response code="404">That device wasn't in a mirror.</response>
    /// <returns>No content.</returns>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult Stop([FromQuery] string? sessionId)
    {
        var id = string.IsNullOrEmpty(sessionId) ? ThisSession()?.Id : sessionId;
        return id is not null && _mirrors.Stop(User.GetUserId(), id) ? NoContent() : NotFound();
    }

    private SessionInfo? ThisSession()
    {
        var userId = User.GetUserId();
        var device = User.GetDeviceId();
        return _sessionManager.Sessions.FirstOrDefault(s => s.UserId.Equals(userId) && string.Equals(s.DeviceId, device, StringComparison.Ordinal));
    }
}
