using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Api.Attributes;
using Khylo.Api.Extensions;
using Khylo.Common.Api;
using Khylo.Controller.Library;
using Khylo.Controller.LiveChannels;
using Khylo.Model.LiveChannels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Khylo.Api.Controllers;

/// <summary>
/// Khylo live channels: always-on themed "TV stations" built from the library.
/// </summary>
[Authorize]
[Tags("LiveChannels")]
public class LiveChannelsController : BaseKhyloApiController
{
    private static readonly TimeSpan _maxGuideWindow = TimeSpan.FromHours(48);

    private readonly ILiveChannelManager _liveChannelManager;
    private readonly IUserManager _userManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="LiveChannelsController"/> class.
    /// </summary>
    /// <param name="liveChannelManager">The live channel manager.</param>
    /// <param name="userManager">The user manager.</param>
    public LiveChannelsController(ILiveChannelManager liveChannelManager, IUserManager userManager)
    {
        _liveChannelManager = liveChannelManager;
        _userManager = userManager;
    }

    /// <summary>
    /// Gets whether Live Channels is turned on, so apps can hide it when it is off.
    /// </summary>
    /// <response code="200">The status.</response>
    /// <returns>Whether Live Channels is on.</returns>
    [HttpGet("Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<LiveChannelStatusDto> GetStatus()
        => Ok(new LiveChannelStatusDto { Enabled = _liveChannelManager.IsEnabled });

    /// <summary>
    /// Gets the channels the current user can watch, with their schedule for a time window.
    /// </summary>
    /// <param name="startTime">Window start (UTC). Defaults to now.</param>
    /// <param name="hours">Window length in hours, up to 48. Defaults to 3.</param>
    /// <response code="200">The channels and their programs.</response>
    /// <returns>The guide.</returns>
    [HttpGet("Guide")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<LiveChannelDto>> GetGuide([FromQuery] DateTime? startTime, [FromQuery, Range(0.25, 48)] double? hours)
    {
        var user = _userManager.GetUserById(User.GetUserId());
        if (user is null)
        {
            return Unauthorized();
        }

        var start = (startTime ?? DateTime.UtcNow).ToUniversalTime();
        var length = TimeSpan.FromHours(hours ?? 3);
        if (length > _maxGuideWindow)
        {
            length = _maxGuideWindow;
        }

        return Ok(_liveChannelManager.GetGuide(user, start, start + length));
    }

    /// <summary>
    /// Gets a channel's details: its schedule for the next hours and the titles it rotates through.
    /// </summary>
    /// <param name="channelId">The channel id.</param>
    /// <param name="hours">How many hours of schedule to include, up to 48. Defaults to 12.</param>
    /// <response code="200">Channel found.</response>
    /// <response code="404">The channel does not exist or is not available to the user.</response>
    /// <returns>The channel's schedule and titles.</returns>
    [HttpGet("{channelId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<LiveChannelDetailsDto> GetChannel([FromRoute, Required] Guid channelId, [FromQuery, Range(1, 48)] int? hours)
    {
        var user = _userManager.GetUserById(User.GetUserId());
        if (user is null)
        {
            return Unauthorized();
        }

        var details = _liveChannelManager.GetChannelDetails(user, channelId, DateTime.UtcNow.AddHours(hours ?? 12));
        return details is null ? NotFound() : Ok(details);
    }

    /// <summary>
    /// Gets a channel's logo, such as a TV network's official logo.
    /// </summary>
    /// <param name="channelId">The channel id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Logo returned.</response>
    /// <response code="404">The channel has no logo.</response>
    /// <returns>The logo image.</returns>
    [HttpGet("{channelId}/Logo")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesImageFile]
    public async Task<ActionResult> GetChannelLogo([FromRoute, Required] Guid channelId, CancellationToken cancellationToken)
    {
        var path = await _liveChannelManager.GetChannelLogoPathAsync(channelId, cancellationToken).ConfigureAwait(false);
        if (path is null)
        {
            return NotFound();
        }

        var contentType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".svg" => "image/svg+xml",
            ".jpg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "image/png"
        };
        Response.Headers.CacheControl = "public, max-age=604800";
        return PhysicalFile(path, contentType);
    }

    /// <summary>
    /// Tunes in to a channel: what is on now, how far in, and what follows. Playback of these items
    /// on this device does not change the user's watch progress.
    /// </summary>
    /// <param name="channelId">The channel id.</param>
    /// <response code="200">What to play.</response>
    /// <response code="404">Channel not found or not available to the user.</response>
    /// <returns>The tune result.</returns>
    [HttpPost("{channelId}/Tune")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<LiveChannelTuneResult> Tune([FromRoute, Required] Guid channelId)
    {
        var user = _userManager.GetUserById(User.GetUserId());
        if (user is null)
        {
            return Unauthorized();
        }

        var result = _liveChannelManager.Tune(user, User.GetDeviceId(), channelId);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// Tells the server this device stopped watching channels.
    /// </summary>
    /// <response code="204">Done.</response>
    /// <returns>No content.</returns>
    [HttpPost("Leave")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult Leave()
    {
        var user = _userManager.GetUserById(User.GetUserId());
        if (user is not null)
        {
            _liveChannelManager.Leave(user, User.GetDeviceId());
        }

        return NoContent();
    }

    /// <summary>
    /// Gets all channel definitions, including disabled and empty ones.
    /// </summary>
    /// <response code="200">The definitions.</response>
    /// <returns>The channel definitions.</returns>
    [HttpGet("Definitions")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<LiveChannelDefinition>> GetDefinitions()
    {
        // Other people's personal collection channels stay private, even from administrators
        var userId = User.GetUserId();
        IReadOnlyList<LiveChannelDefinition> definitions = _liveChannelManager.GetDefinitions()
            .Where(d => d.OwnerUserId is null || d.OwnerUserId.Value.Equals(userId))
            .ToList();
        return Ok(definitions);
    }

    /// <summary>
    /// Gets the master switch and every channel with where it stands and how much content it has.
    /// </summary>
    /// <response code="200">The admin view.</response>
    /// <returns>The master switch and channels.</returns>
    [HttpGet("Admin")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<LiveChannelAdminDto> GetAdminStatus()
    {
        var user = _userManager.GetUserById(User.GetUserId());
        return user is null ? Unauthorized() : Ok(_liveChannelManager.GetAdminStatus(user));
    }

    /// <summary>
    /// Turns Live Channels on or off for the whole server.
    /// </summary>
    /// <param name="status">Whether to turn it on.</param>
    /// <response code="204">Done.</response>
    /// <returns>No content.</returns>
    [HttpPost("Admin/Enabled")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult SetEnabled([FromBody, Required] LiveChannelStatusDto status)
    {
        _liveChannelManager.SetEnabled(status.Enabled);
        return NoContent();
    }

    /// <summary>
    /// Turns one or more channels on or off.
    /// </summary>
    /// <param name="request">The channels and whether to turn them on.</param>
    /// <response code="204">Done.</response>
    /// <returns>No content.</returns>
    [HttpPost("Admin/Channels")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult SetChannelsEnabled([FromBody, Required] LiveChannelToggleRequest request)
    {
        _liveChannelManager.SetChannelsEnabled(request.ChannelIds, request.Enabled);
        return NoContent();
    }

    /// <summary>
    /// Discards all channel schedules and rebuilds them from the current library.
    /// </summary>
    /// <response code="204">Schedules reset.</response>
    /// <returns>No content.</returns>
    [HttpPost("Reset")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult ResetSchedules()
    {
        _liveChannelManager.ResetSchedules();
        return NoContent();
    }
}
