using System.Collections.Generic;
using Khylo.Api.Extensions;
using Khylo.Common.Api;
using Khylo.Controller.Listening;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Khylo.Api.Controllers;

/// <summary>
/// A person's own listening and watching history: the switch that turns recording off, deleting it, and plays made offline.
/// Everything here is about the signed-in person only.
/// </summary>
[Route("Khylo/Listening")]
[Authorize]
public class KhyloListeningController : BaseKhyloApiController
{
    private readonly IListeningHistory _history;

    /// <summary>
    /// Initializes a new instance of the <see cref="KhyloListeningController"/> class.
    /// </summary>
    /// <param name="history">The listening history.</param>
    public KhyloListeningController(IListeningHistory history)
    {
        _history = history;
    }

    /// <summary>
    /// Gets whether the signed-in person's listening is being recorded.
    /// </summary>
    /// <response code="200">The current setting.</response>
    /// <returns>Whether recording is on.</returns>
    [HttpGet("Settings")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<ListeningSettingsDto> GetSettings()
        => new ListeningSettingsDto { Enabled = _history.IsRecordingEnabled(User.GetUserId()) };

    /// <summary>
    /// Turns recording on or off for the signed-in person. Turning it off keeps what is already stored.
    /// </summary>
    /// <param name="settings">The setting.</param>
    /// <response code="204">Saved.</response>
    /// <returns>An empty result.</returns>
    [HttpPost("Settings")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult SetSettings([FromBody] ListeningSettingsDto settings)
    {
        _history.SetRecordingEnabled(User.GetUserId(), settings.Enabled);
        return NoContent();
    }

    /// <summary>
    /// Deletes all of the signed-in person's history.
    /// </summary>
    /// <response code="204">Deleted.</response>
    /// <returns>An empty result.</returns>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult DeleteHistory()
    {
        _history.DeleteForUser(User.GetUserId());
        return NoContent();
    }

    /// <summary>
    /// Stores plays made with no connection (downloaded songs). A play already stored for the same item and start time is ignored.
    /// </summary>
    /// <param name="plays">The plays.</param>
    /// <response code="200">How many were new.</response>
    /// <returns>The number of new plays.</returns>
    [HttpPost("Plays")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<int> AddPlays([FromBody] IReadOnlyList<OfflinePlay> plays)
        => _history.AddOfflinePlays(User.GetUserId(), plays);
}
