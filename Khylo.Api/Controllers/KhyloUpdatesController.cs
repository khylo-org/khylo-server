using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Khylo.Api.Extensions;
using Khylo.Common.Api;
using Khylo.Controller.Library;
using Khylo.Controller.Updates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Khylo.Api.Controllers;

/// <summary>
/// Khylo updating itself: what's available, which channel to follow, whether to install by itself, and installing now.
/// </summary>
[Route("Khylo/Updates")]
[Authorize(Policy = Policies.RequiresElevation)]
public class KhyloUpdatesController : BaseKhyloApiController
{
    private readonly IKhyloUpdateService _updates;
    private readonly IUserManager _users;

    /// <summary>
    /// Initializes a new instance of the <see cref="KhyloUpdatesController"/> class.
    /// </summary>
    /// <param name="updates">The update service.</param>
    /// <param name="users">The user manager.</param>
    public KhyloUpdatesController(IKhyloUpdateService updates, IUserManager users)
    {
        _updates = updates;
        _users = users;
    }

    /// <summary>
    /// Gets what's known about updates.
    /// </summary>
    /// <response code="200">What's known.</response>
    /// <returns>The update status.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<KhyloUpdateStatus> GetStatus() => _updates.GetStatus();

    /// <summary>
    /// Looks for a newer build now.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The status after looking.</response>
    /// <returns>The status once the release page has been asked.</returns>
    [HttpPost("Check")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<KhyloUpdateStatus>> Check(CancellationToken cancellationToken)
        => await _updates.CheckAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Sets the channel (stable or dev) and whether updates install by themselves at the quiet hour.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <response code="200">The status with the new settings.</response>
    /// <returns>The status with the settings applied.</returns>
    [HttpPost("Settings")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<KhyloUpdateStatus>> SetSettings([FromBody, Required] KhyloUpdateSettings settings)
    {
        var before = _updates.GetStatus();
        var status = _updates.SetSettings(settings.Channel ?? before.Channel, settings.AutoInstall);
        if (!string.Equals(before.Channel, status.Channel, StringComparison.Ordinal))
        {
            // The new channel may have a different newest build
            status = await _updates.CheckAsync(CancellationToken.None).ConfigureAwait(false);
        }

        return status;
    }

    /// <summary>
    /// Ignores the build on offer.
    /// </summary>
    /// <response code="200">The status without that build on offer.</response>
    /// <returns>The status after skipping.</returns>
    [HttpPost("Skip")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<KhyloUpdateStatus> Skip() => _updates.SkipLatest();

    /// <summary>
    /// Installs the build on offer. Khylo restarts as part of it, so the answer comes first.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="202">The update was started.</response>
    /// <response code="400">It can't be installed; the message says why.</response>
    /// <returns>Accepted.</returns>
    [HttpPost("Install")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> Install(CancellationToken cancellationToken)
    {
        var name = _users.GetUserById(User.GetUserId())?.Username ?? "an administrator";
        try
        {
            await _updates.InstallAsync(name, cancellationToken).ConfigureAwait(false);
            return Accepted();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    /// <summary>
    /// The settings that can be changed.
    /// </summary>
    public class KhyloUpdateSettings
    {
        /// <summary>Gets or sets the channel: "stable" or "dev".</summary>
        public string? Channel { get; set; }

        /// <summary>Gets or sets a value indicating whether updates install by themselves at the quiet hour.</summary>
        public bool AutoInstall { get; set; }
    }
}
