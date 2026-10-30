using System;
using System.Collections.Generic;
using System.Linq;

namespace Khylo.Server.Implementations.LiveChannels;

/// <summary>
/// Lays out a channel's programs back to back, broadcast style. Deterministic: the same state and
/// content always produce the same schedule.
/// <list type="bullet">
/// <item>Every movie and series in the channel airs once per "deck" before anything repeats.</item>
/// <item>Each deck is a shuffle seeded by the channel id and deck number.</item>
/// <item>A series airs a block of episodes in order, then the channel moves on to another one:
/// short seasons air start to finish, long seasons are split into halves (or thirds…).</item>
/// <item>Series continue where they left off, wrapping back to the first season at the end.</item>
/// </list>
/// </summary>
internal static class LiveChannelScheduler
{
    /// <summary>
    /// Seasons with more episodes than this are split into parts (22 episodes air as two halves of 11).
    /// </summary>
    public const int MaxEpisodesPerBlock = 13;

    // Safety stop for a single extension
    private const int MaxProgramsPerExtension = 5000;

    /// <summary>
    /// Programs ending longer ago than this are dropped.
    /// </summary>
    public static readonly TimeSpan History = TimeSpan.FromHours(3);

    /// <summary>
    /// The longest a single series block may run, so hour-long dramas don't take over a whole day.
    /// </summary>
    public static readonly TimeSpan MaxBlockDuration = TimeSpan.FromHours(6);

    /// <summary>
    /// Extends a schedule so it runs until at least <paramref name="untilUtc"/>.
    /// </summary>
    /// <param name="state">The channel schedule state, updated in place.</param>
    /// <param name="channelId">The channel id, used to seed shuffles.</param>
    /// <param name="units">The channel's content, keyed by unit key.</param>
    /// <param name="nowUtc">The current time.</param>
    /// <param name="untilUtc">How far ahead the schedule must reach.</param>
    /// <returns>True if the state changed.</returns>
    public static bool Extend(
        LiveChannelScheduleState state,
        Guid channelId,
        IReadOnlyDictionary<string, LiveChannelUnit> units,
        DateTime nowUtc,
        DateTime untilUtc)
    {
        var changed = PruneHistory(state, nowUtc);
        if (units.Count == 0)
        {
            return changed;
        }

        // Start fresh (on the hour) when there is no schedule or the server was off for a while;
        // the rotation itself continues where it left off.
        DateTime nextStart;
        if (state.Programs.Count == 0)
        {
            nextStart = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, nowUtc.Hour, 0, 0, DateTimeKind.Utc);
        }
        else
        {
            nextStart = state.Programs[^1].EndUtc;
        }

        var added = 0;
        while (nextStart < untilUtc && added < MaxProgramsPerExtension)
        {
            var unit = NextUnit(state, channelId, units);
            if (unit is null)
            {
                break;
            }

            var block = unit.IsSeries ? NextSeriesBlock(state, unit) : [unit.Items[0]];
            foreach (var item in block)
            {
                state.Programs.Add(new LiveChannelScheduledProgram
                {
                    ItemId = item.ItemId,
                    StartUtc = nextStart,
                    DurationTicks = item.RunTimeTicks
                });
                nextStart = nextStart.AddTicks(item.RunTimeTicks);
                added++;
            }
        }

        return changed || added > 0;
    }

    /// <summary>
    /// Finds the index of the program airing at a time.
    /// </summary>
    /// <param name="state">The schedule state.</param>
    /// <param name="timeUtc">The time.</param>
    /// <returns>The program index, or -1.</returns>
    public static int FindProgramIndex(LiveChannelScheduleState state, DateTime timeUtc)
    {
        var programs = state.Programs;
        int low = 0, high = programs.Count - 1;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            if (programs[mid].EndUtc <= timeUtc)
            {
                low = mid + 1;
            }
            else if (programs[mid].StartUtc > timeUtc)
            {
                high = mid - 1;
            }
            else
            {
                return mid;
            }
        }

        return -1;
    }

    private static bool PruneHistory(LiveChannelScheduleState state, DateTime nowUtc)
    {
        var programs = state.Programs;
        if (programs.Count == 0)
        {
            return false;
        }

        // After a long gap (server off) the old schedule is useless; restart from now.
        if (programs[^1].EndUtc < nowUtc - History)
        {
            programs.Clear();
            return true;
        }

        var cutoff = nowUtc - History;
        var removeCount = 0;
        while (removeCount < programs.Count && programs[removeCount].EndUtc < cutoff)
        {
            removeCount++;
        }

        if (removeCount == 0)
        {
            return false;
        }

        programs.RemoveRange(0, removeCount);
        return true;
    }

    private static LiveChannelUnit? NextUnit(LiveChannelScheduleState state, Guid channelId, IReadOnlyDictionary<string, LiveChannelUnit> units)
    {
        // At most one fresh deck per call, so units missing from the library can't loop forever
        var dealt = false;
        while (true)
        {
            if (state.DeckPosition >= state.Deck.Count)
            {
                if (dealt)
                {
                    return null;
                }

                DealDeck(state, channelId, units);
                dealt = true;
            }

            var key = state.Deck[state.DeckPosition++];
            if (units.TryGetValue(key, out var unit))
            {
                return unit;
            }
        }
    }

    private static void DealDeck(LiveChannelScheduleState state, Guid channelId, IReadOnlyDictionary<string, LiveChannelUnit> units)
    {
        var previousLast = state.Deck.Count > 0 ? state.Deck[^1] : null;

        var deck = units.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        state.DeckCycle++;
        var random = new Random(Seed(channelId, state.DeckCycle));
        for (var i = deck.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }

        // Never air the same thing twice in a row across a deck boundary
        if (deck.Count > 1 && string.Equals(deck[0], previousLast, StringComparison.Ordinal))
        {
            (deck[0], deck[^1]) = (deck[^1], deck[0]);
        }

        state.Deck = deck;
        state.DeckPosition = 0;
    }

    /// <summary>
    /// The next block of a series: from where it left off to the end of the current season part.
    /// </summary>
    /// <param name="state">The schedule state; the series' position is advanced.</param>
    /// <param name="unit">The series.</param>
    /// <returns>The episodes to air, in order.</returns>
    internal static List<LiveChannelUnitItem> NextSeriesBlock(LiveChannelScheduleState state, LiveChannelUnit unit)
    {
        var items = unit.Items;
        var start = 0;
        if (state.LastEpisodes.TryGetValue(unit.Key, out var lastId))
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].ItemId.Equals(lastId))
                {
                    start = (i + 1) % items.Count;
                    break;
                }
            }
        }

        // The season the block starts in (episodes are ordered, so a season is contiguous)
        var season = items[start].SeasonNumber;
        var seasonStart = start;
        while (seasonStart > 0 && items[seasonStart - 1].SeasonNumber == season)
        {
            seasonStart--;
        }

        var seasonEnd = start;
        while (seasonEnd < items.Count && items[seasonEnd].SeasonNumber == season)
        {
            seasonEnd++;
        }

        // Split long seasons into equal parts and air up to the end of the current part
        var seasonLength = seasonEnd - seasonStart;
        var parts = (seasonLength + MaxEpisodesPerBlock - 1) / MaxEpisodesPerBlock;
        var partSize = (seasonLength + parts - 1) / parts;
        var partEnd = Math.Min(seasonEnd, seasonStart + ((((start - seasonStart) / partSize) + 1) * partSize));

        var block = new List<LiveChannelUnitItem>();
        long blockTicks = 0;
        for (var i = start; i < partEnd; i++)
        {
            if (block.Count > 0 && blockTicks + items[i].RunTimeTicks > MaxBlockDuration.Ticks)
            {
                break;
            }

            block.Add(items[i]);
            blockTicks += items[i].RunTimeTicks;
        }

        state.LastEpisodes[unit.Key] = block[^1].ItemId;
        return block;
    }

    // FNV-1a over the channel id and cycle: stable across runs and .NET versions
    private static int Seed(Guid channelId, int cycle)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var b in channelId.ToByteArray())
            {
                hash = (hash ^ b) * 16777619u;
            }

            foreach (var b in BitConverter.GetBytes(cycle))
            {
                hash = (hash ^ b) * 16777619u;
            }

            return (int)hash;
        }
    }
}
