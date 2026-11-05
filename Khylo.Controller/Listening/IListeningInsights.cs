using System;

namespace Khylo.Controller.Listening;

/// <summary>
/// What the server owner can see of what is played and watched, the way a media server's dashboard shows it. It gives totals,
/// lists and history, and never a person's own weekly, quarterly or yearly review. People who turned recording off are left out.
/// </summary>
public interface IListeningInsights
{
    /// <summary>
    /// Gets the overview: popular items, time per person, platforms, play methods, resolutions and the busiest moment.
    /// </summary>
    /// <param name="days">How many days back, or 0 for all time.</param>
    /// <param name="timeZone">An IANA time zone name, used to group days and hours.</param>
    /// <param name="kind">all, video or audio.</param>
    /// <returns>The overview.</returns>
    object Overview(int days, string? timeZone, string? kind);

    /// <summary>
    /// Gets one person's plain activity: time spent, views and plays, their top items, platforms and recent history.
    /// </summary>
    /// <param name="userId">The person.</param>
    /// <param name="days">How many days back, or 0 for all time.</param>
    /// <returns>The activity.</returns>
    object UserActivity(Guid userId, int days);

    /// <summary>
    /// Gets plays and views across everyone, newest first.
    /// </summary>
    /// <param name="before">Only sessions before this moment.</param>
    /// <param name="limit">The page size.</param>
    /// <param name="userId">Only this person, or null for everyone.</param>
    /// <param name="kind">all, video or audio.</param>
    /// <returns>The page.</returns>
    object History(DateTime? before, int limit, Guid? userId, string? kind);

    /// <summary>
    /// Gets the server's own measurements over time: processor, memory, streams, transcodes and bandwidth.
    /// </summary>
    /// <param name="from">Start of the range.</param>
    /// <param name="to">End of the range.</param>
    /// <param name="stepMinutes">The size of each point in minutes.</param>
    /// <returns>The points.</returns>
    object Resources(DateTime? from, DateTime? to, int stepMinutes);

    /// <summary>
    /// Gets the size of each library, what was added each week for the last twelve weeks, and the total size.
    /// </summary>
    /// <returns>The library numbers.</returns>
    object Library();
}
