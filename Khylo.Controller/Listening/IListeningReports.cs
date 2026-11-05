using System;

namespace Khylo.Controller.Listening;

/// <summary>
/// A person's own reviews of their listening and watching: the weekly summary, the history list, the quarterly reviews and the
/// yearly Wrapped. Every method answers for the person it is given and no one else.
/// </summary>
public interface IListeningReports
{
    /// <summary>
    /// Gets the summary of a range of local days.
    /// </summary>
    /// <param name="userId">The person.</param>
    /// <param name="from">The first local day.</param>
    /// <param name="to">The last local day (inclusive).</param>
    /// <param name="timeZone">An IANA time zone name.</param>
    /// <param name="video">True for movies and shows, false for music.</param>
    /// <returns>The summary.</returns>
    object Summary(Guid userId, DateOnly from, DateOnly to, string? timeZone, bool video);

    /// <summary>
    /// Gets a page of the history, newest first.
    /// </summary>
    /// <param name="userId">The person.</param>
    /// <param name="before">Only sessions before this moment.</param>
    /// <param name="limit">The page size.</param>
    /// <param name="video">True for movies and shows, false for music.</param>
    /// <returns>The page.</returns>
    object History(Guid userId, DateTime? before, int limit, bool video);

    /// <summary>
    /// Gets a year in review. It opens the Monday after Black Friday, and once built it never changes.
    /// </summary>
    /// <param name="userId">The person.</param>
    /// <param name="year">The year.</param>
    /// <param name="timeZone">An IANA time zone name.</param>
    /// <param name="video">True for the video review (Rewind).</param>
    /// <param name="preview">True to skip the opening date (Dev builds only).</param>
    /// <returns>The review.</returns>
    object Wrapped(Guid userId, int year, string? timeZone, bool video, bool preview);

    /// <summary>
    /// Gets a quarterly review. It opens the first Monday after the quarter ends, and once built it never changes.
    /// </summary>
    /// <param name="userId">The person.</param>
    /// <param name="year">The year.</param>
    /// <param name="quarter">1 to 4.</param>
    /// <param name="timeZone">An IANA time zone name.</param>
    /// <param name="video">True for movies and shows.</param>
    /// <param name="preview">True to skip the opening date (Dev builds only).</param>
    /// <returns>The review.</returns>
    object Quarter(Guid userId, int year, int quarter, string? timeZone, bool video, bool preview);

    /// <summary>
    /// Lists the quarters that have data and whether each is open yet.
    /// </summary>
    /// <param name="userId">The person.</param>
    /// <param name="timeZone">An IANA time zone name.</param>
    /// <param name="video">True for movies and shows.</param>
    /// <returns>The quarters.</returns>
    object Quarters(Guid userId, string? timeZone, bool video);

    /// <summary>
    /// Fills a person's own history with believable made-up listening, marked as test data (Dev builds only).
    /// </summary>
    /// <param name="userId">The person.</param>
    /// <param name="days">How many days back.</param>
    /// <param name="video">True for movies and shows.</param>
    /// <returns>How many sessions were added.</returns>
    int SeedTestData(Guid userId, int days, bool video);

    /// <summary>
    /// Removes a person's made-up test history and the reviews built from it, and the pretend household members' too.
    /// </summary>
    /// <param name="userId">The person.</param>
    void DeleteTestData(Guid userId);
}
