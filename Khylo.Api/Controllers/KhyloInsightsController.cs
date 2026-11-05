using System;
using Khylo.Common.Api;
using Khylo.Controller.Listening;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Khylo.Api.Controllers;

/// <summary>
/// What the server owner can see of what is played and watched, and how busy the server has been. Administrators only. It gives
/// totals, lists and history, and never a person's own weekly, quarterly or yearly review.
/// </summary>
[Route("Khylo/Manage/Insights")]
[Authorize(Policy = Policies.RequiresElevation)]
public class KhyloInsightsController : BaseKhyloApiController
{
    private readonly IListeningInsights _insights;

    /// <summary>
    /// Initializes a new instance of the <see cref="KhyloInsightsController"/> class.
    /// </summary>
    /// <param name="insights">The insights.</param>
    public KhyloInsightsController(IListeningInsights insights)
    {
        _insights = insights;
    }

    /// <summary>
    /// Gets popular items, time per person, platforms, play methods, resolutions and the busiest moment.
    /// </summary>
    /// <param name="days">7, 30, 90, 365, or 0 for all time.</param>
    /// <param name="tz">An IANA time zone name.</param>
    /// <param name="kind">all (the default), video or audio.</param>
    /// <response code="200">The overview.</response>
    /// <returns>Totals, top lists and breakdowns for the range.</returns>
    [HttpGet("Overview")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> GetOverview([FromQuery] int days = 7, [FromQuery] string? tz = null, [FromQuery] string? kind = null)
        => _insights.Overview(days, tz, kind);

    /// <summary>
    /// Gets one person's plain activity: time spent, views, their top items, platforms and recent history. Not their review.
    /// </summary>
    /// <param name="userId">The person.</param>
    /// <param name="days">How many days back, or 0 for all time.</param>
    /// <response code="200">The activity.</response>
    /// <returns>The person's activity.</returns>
    [HttpGet("Users/{userId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> GetUserActivity([FromRoute] Guid userId, [FromQuery] int days = 30)
        => _insights.UserActivity(userId, days);

    /// <summary>
    /// Gets plays and views across everyone, newest first.
    /// </summary>
    /// <param name="before">Only sessions before this moment.</param>
    /// <param name="limit">The page size, up to 200.</param>
    /// <param name="userId">Only this person.</param>
    /// <param name="kind">all (the default), video or audio.</param>
    /// <response code="200">The page.</response>
    /// <returns>The sessions on this page and where the next page starts.</returns>
    [HttpGet("History")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> GetHistory([FromQuery] DateTime? before = null, [FromQuery] int limit = 50, [FromQuery] Guid? userId = null, [FromQuery] string? kind = null)
        => _insights.History(before, limit, userId, kind);

    /// <summary>
    /// Gets the server's own measurements over time.
    /// </summary>
    /// <param name="from">Start of the range (default: a day ago).</param>
    /// <param name="to">End of the range (default: now).</param>
    /// <param name="step">Minutes in each point.</param>
    /// <response code="200">The points.</response>
    /// <returns>Processor, memory, streams, transcodes and bandwidth per point.</returns>
    [HttpGet("Resources")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> GetResources([FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null, [FromQuery] int step = 60)
        => _insights.Resources(from, to, step);

    /// <summary>
    /// Gets items per library, what was added each week for the last twelve weeks, and the total size.
    /// </summary>
    /// <response code="200">The library numbers.</response>
    /// <returns>Items per library, weekly additions and total size.</returns>
    [HttpGet("Library")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> GetLibrary()
        => _insights.Library();
}
