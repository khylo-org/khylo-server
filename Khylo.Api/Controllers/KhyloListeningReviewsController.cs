using System;
using Khylo.Api.Extensions;
using Khylo.Common;
using Khylo.Common.Api;
using Khylo.Controller.Listening;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Khylo.Api.Controllers;

/// <summary>
/// A person's own weekly stats, history, quarterly reviews and yearly Wrapped. Every answer is about the signed-in person only:
/// there is no way to ask for anyone else's, and an administrator gets their own like everyone else.
/// </summary>
[Route("Khylo/Listening")]
[Authorize]
public class KhyloListeningReviewsController : BaseKhyloApiController
{
    private readonly IListeningReports _reports;

    /// <summary>
    /// Initializes a new instance of the <see cref="KhyloListeningReviewsController"/> class.
    /// </summary>
    /// <param name="reports">The reports.</param>
    public KhyloListeningReviewsController(IListeningReports reports)
    {
        _reports = reports;
    }

    private static bool IsDevBuild => KhyloBuildInfo.Number.StartsWith("DEV", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets a summary of any range of local days: time, plays, daily bars, top lists and what is new.
    /// </summary>
    /// <param name="from">The first local day.</param>
    /// <param name="to">The last local day (inclusive).</param>
    /// <param name="tz">An IANA time zone name such as America/Los_Angeles.</param>
    /// <param name="kind">music (the default) or video.</param>
    /// <response code="200">The summary.</response>
    /// <returns>Time listened, plays and the top lists for the range.</returns>
    [HttpGet("Summary")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> GetSummary([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] string? tz, [FromQuery] string? kind)
        => _reports.Summary(User.GetUserId(), from, to, tz, IsVideo(kind));

    /// <summary>
    /// Gets a page of what was played, newest first.
    /// </summary>
    /// <param name="before">Only plays before this moment (use the Next value of the last page).</param>
    /// <param name="limit">The page size, up to 200.</param>
    /// <param name="kind">music (the default) or video.</param>
    /// <response code="200">The page.</response>
    /// <returns>The plays on this page and where the next page starts.</returns>
    [HttpGet("History")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> GetHistory([FromQuery] DateTime? before, [FromQuery] int limit = 50, [FromQuery] string? kind = null)
        => _reports.History(User.GetUserId(), before, limit, IsVideo(kind));

    /// <summary>
    /// Gets the year in review. It opens the Monday after Black Friday.
    /// </summary>
    /// <param name="year">The year.</param>
    /// <param name="tz">An IANA time zone name.</param>
    /// <param name="kind">music (the default) or video.</param>
    /// <param name="preview">Skip the opening date (Dev builds only).</param>
    /// <response code="200">The review, or when it opens.</response>
    /// <returns>The review.</returns>
    [HttpGet("Wrapped")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> GetWrapped([FromQuery] int year, [FromQuery] string? tz, [FromQuery] string? kind, [FromQuery] bool preview = false)
        => _reports.Wrapped(User.GetUserId(), year, tz, IsVideo(kind), preview);

    /// <summary>
    /// Gets a quarterly review. It opens the first Monday after the quarter ends.
    /// </summary>
    /// <param name="year">The year.</param>
    /// <param name="quarter">1 to 4.</param>
    /// <param name="tz">An IANA time zone name.</param>
    /// <param name="kind">music (the default) or video.</param>
    /// <param name="preview">Skip the opening date (Dev builds only).</param>
    /// <response code="200">The review, or when it opens.</response>
    /// <returns>The review.</returns>
    [HttpGet("Quarter")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> GetQuarter([FromQuery] int year, [FromQuery] int quarter, [FromQuery] string? tz, [FromQuery] string? kind, [FromQuery] bool preview = false)
        => _reports.Quarter(User.GetUserId(), year, quarter, tz, IsVideo(kind), preview);

    /// <summary>
    /// Lists the quarters that have data and whether each is open yet.
    /// </summary>
    /// <param name="kind">music (the default) or video.</param>
    /// <param name="tz">An IANA time zone name.</param>
    /// <response code="200">The quarters.</response>
    /// <returns>One entry for each quarter with data.</returns>
    [HttpGet("Quarters")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> GetQuarters([FromQuery] string? kind, [FromQuery] string? tz)
        => _reports.Quarters(User.GetUserId(), tz, IsVideo(kind));

    /// <summary>
    /// Fills the signed-in person's own history with believable made-up listening (Dev builds only).
    /// </summary>
    /// <param name="days">How many days back.</param>
    /// <param name="kind">music (the default) or video; video also adds pretend household members.</param>
    /// <response code="200">How many plays were added.</response>
    /// <response code="403">Not available in a Stable build.</response>
    /// <returns>The number added.</returns>
    [HttpPost("Debug/Seed")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<int> SeedTestData([FromQuery] int days = 60, [FromQuery] string? kind = null)
        => IsDevBuild ? _reports.SeedTestData(User.GetUserId(), days, IsVideo(kind)) : Forbid();

    /// <summary>
    /// Removes the made-up test history and the reviews built from it (Dev builds only).
    /// </summary>
    /// <response code="204">Removed.</response>
    /// <response code="403">Not available in a Stable build.</response>
    /// <returns>An empty result.</returns>
    [HttpDelete("Debug")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult DeleteTestData()
    {
        if (!IsDevBuild)
        {
            return Forbid();
        }

        _reports.DeleteTestData(User.GetUserId());
        return NoContent();
    }

    private static bool IsVideo(string? kind) => string.Equals(kind, "video", StringComparison.OrdinalIgnoreCase);
}
