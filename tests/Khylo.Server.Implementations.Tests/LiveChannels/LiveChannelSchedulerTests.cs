using System;
using System.Collections.Generic;
using System.Linq;
using Khylo.Server.Implementations.LiveChannels;
using Xunit;

namespace Khylo.Server.Implementations.Tests.LiveChannels;

public class LiveChannelSchedulerTests
{
    private static readonly Guid _channelId = Guid.Parse("6a1f0f3e-2a55-4c3b-9d7e-1d2c3b4a5f60");
    private static readonly DateTime _now = new(2026, 10, 8, 14, 37, 12, DateTimeKind.Utc);

    private static Dictionary<string, LiveChannelUnit> CreateUnits(int movies, int series, int episodesPerSeries)
    {
        var units = new Dictionary<string, LiveChannelUnit>(StringComparer.Ordinal);
        for (var m = 0; m < movies; m++)
        {
            var key = "m:" + m;
            units[key] = new LiveChannelUnit(key, false, [new LiveChannelUnitItem(Guid.NewGuid(), TimeSpan.FromMinutes(100 + m).Ticks)]);
        }

        for (var s = 0; s < series; s++)
        {
            var key = "s:" + s;
            units[key] = CreateSeries(key, 22, episodesPerSeries);
        }

        return units;
    }

    private static LiveChannelUnit CreateSeries(string key, int episodeMinutes, params int[] seasonSizes)
    {
        var episodes = new List<LiveChannelUnitItem>();
        for (var season = 0; season < seasonSizes.Length; season++)
        {
            for (var e = 0; e < seasonSizes[season]; e++)
            {
                episodes.Add(new LiveChannelUnitItem(Guid.NewGuid(), TimeSpan.FromMinutes(episodeMinutes).Ticks, season + 1));
            }
        }

        return new LiveChannelUnit(key, true, episodes);
    }

    [Fact]
    public void Extend_FillsUntilRequestedTime_BackToBack()
    {
        var units = CreateUnits(5, 3, 10);
        var state = new LiveChannelScheduleState();
        var until = _now.AddHours(24);

        Assert.True(LiveChannelScheduler.Extend(state, _channelId, units, _now, until));

        Assert.Equal(new DateTime(2026, 10, 8, 14, 0, 0, DateTimeKind.Utc), state.Programs[0].StartUtc);
        Assert.True(state.Programs[^1].EndUtc >= until);
        for (var i = 1; i < state.Programs.Count; i++)
        {
            Assert.Equal(state.Programs[i - 1].EndUtc, state.Programs[i].StartUtc);
        }
    }

    [Fact]
    public void Extend_IsDeterministic()
    {
        var units = CreateUnits(8, 4, 6);
        var first = new LiveChannelScheduleState();
        var second = new LiveChannelScheduleState();

        LiveChannelScheduler.Extend(first, _channelId, units, _now, _now.AddHours(48));
        LiveChannelScheduler.Extend(second, _channelId, units, _now, _now.AddHours(48));

        Assert.Equal(first.Programs.Select(p => p.ItemId), second.Programs.Select(p => p.ItemId));
    }

    [Fact]
    public void Extend_InTwoSteps_MatchesOneStep()
    {
        var units = CreateUnits(8, 4, 6);
        var oneStep = new LiveChannelScheduleState();
        var twoSteps = new LiveChannelScheduleState();

        LiveChannelScheduler.Extend(oneStep, _channelId, units, _now, _now.AddHours(48));
        LiveChannelScheduler.Extend(twoSteps, _channelId, units, _now, _now.AddHours(20));
        LiveChannelScheduler.Extend(twoSteps, _channelId, units, _now, _now.AddHours(48));

        var compared = Math.Min(oneStep.Programs.Count, twoSteps.Programs.Count);
        Assert.Equal(
            oneStep.Programs.Take(compared).Select(p => p.ItemId),
            twoSteps.Programs.Take(compared).Select(p => p.ItemId));
    }

    [Fact]
    public void Extend_EveryTitleAirsOnceBeforeAnyRepeats()
    {
        var units = CreateUnits(12, 0, 0);
        var state = new LiveChannelScheduleState();

        LiveChannelScheduler.Extend(state, _channelId, units, _now, _now.AddHours(48));

        var firstCycle = state.Programs.Take(units.Count).Select(p => p.ItemId).ToList();
        Assert.Equal(units.Count, firstCycle.Distinct().Count());
        for (var i = 1; i < state.Programs.Count; i++)
        {
            Assert.NotEqual(state.Programs[i - 1].ItemId, state.Programs[i].ItemId);
        }
    }

    [Fact]
    public void Extend_SeriesAirInEpisodeOrder()
    {
        var units = CreateUnits(0, 3, 8);
        var state = new LiveChannelScheduleState();

        LiveChannelScheduler.Extend(state, _channelId, units, _now, _now.AddHours(24));

        foreach (var unit in units.Values)
        {
            var aired = state.Programs
                .Select(p => unit.Items.ToList().FindIndex(i => i.ItemId.Equals(p.ItemId)))
                .Where(index => index >= 0)
                .ToList();
            Assert.NotEmpty(aired);
            for (var i = 0; i < aired.Count; i++)
            {
                Assert.Equal(i % unit.Items.Count, aired[i]);
            }
        }
    }

    [Fact]
    public void NextSeriesBlock_ShortSeason_AirsWholeSeason()
    {
        var series = CreateSeries("s:short", 25, 8, 10);
        var state = new LiveChannelScheduleState();

        var first = LiveChannelScheduler.NextSeriesBlock(state, series);
        var second = LiveChannelScheduler.NextSeriesBlock(state, series);

        Assert.Equal(series.Items.Take(8).Select(i => i.ItemId), first.Select(i => i.ItemId));
        Assert.Equal(series.Items.Skip(8).Take(10).Select(i => i.ItemId), second.Select(i => i.ItemId));
    }

    [Fact]
    public void NextSeriesBlock_LongSeason_SplitsIntoHalves()
    {
        var series = CreateSeries("s:long", 22, 22, 22);
        var state = new LiveChannelScheduleState();

        var firstHalf = LiveChannelScheduler.NextSeriesBlock(state, series);
        var secondHalf = LiveChannelScheduler.NextSeriesBlock(state, series);
        var nextSeason = LiveChannelScheduler.NextSeriesBlock(state, series);

        Assert.Equal(11, firstHalf.Count);
        Assert.Equal(11, secondHalf.Count);
        Assert.Equal(series.Items[11].ItemId, secondHalf[0].ItemId);
        Assert.All(nextSeason, e => Assert.Equal(2, e.SeasonNumber));
        Assert.Equal(11, nextSeason.Count);
    }

    [Fact]
    public void NextSeriesBlock_LongEpisodes_CappedAtMaxBlockDuration()
    {
        var series = CreateSeries("s:drama", 60, 10);
        var state = new LiveChannelScheduleState();

        var first = LiveChannelScheduler.NextSeriesBlock(state, series);
        var rest = LiveChannelScheduler.NextSeriesBlock(state, series);

        Assert.Equal(6, first.Count);
        Assert.Equal(4, rest.Count);
        Assert.Equal(series.Items[6].ItemId, rest[0].ItemId);
    }

    [Fact]
    public void NextSeriesBlock_AfterLastEpisode_StartsOver()
    {
        var series = CreateSeries("s:mini", 45, 4);
        var state = new LiveChannelScheduleState();

        LiveChannelScheduler.NextSeriesBlock(state, series);
        var again = LiveChannelScheduler.NextSeriesBlock(state, series);

        Assert.Equal(series.Items[0].ItemId, again[0].ItemId);
    }

    [Fact]
    public void Extend_SkipsTitlesRemovedFromLibrary()
    {
        var units = CreateUnits(6, 0, 0);
        var state = new LiveChannelScheduleState();
        LiveChannelScheduler.Extend(state, _channelId, units, _now, _now.AddHours(4));

        var removed = units.Keys.First();
        var removedItem = units[removed].Items[0].ItemId;
        units.Remove(removed);
        var countBefore = state.Programs.Count;
        LiveChannelScheduler.Extend(state, _channelId, units, _now, _now.AddHours(48));

        Assert.DoesNotContain(state.Programs.Skip(countBefore), p => p.ItemId.Equals(removedItem));
    }

    [Fact]
    public void Extend_AfterLongGap_RestartsAtCurrentHour()
    {
        var units = CreateUnits(6, 2, 4);
        var state = new LiveChannelScheduleState();
        LiveChannelScheduler.Extend(state, _channelId, units, _now, _now.AddHours(6));

        var later = _now.AddDays(3);
        LiveChannelScheduler.Extend(state, _channelId, units, later, later.AddHours(6));

        Assert.Equal(new DateTime(2026, 10, 11, 14, 0, 0, DateTimeKind.Utc), state.Programs[0].StartUtc);
    }

    [Fact]
    public void FindProgramIndex_FindsProgramOnAir()
    {
        var units = CreateUnits(4, 2, 5);
        var state = new LiveChannelScheduleState();
        LiveChannelScheduler.Extend(state, _channelId, units, _now, _now.AddHours(12));

        var index = LiveChannelScheduler.FindProgramIndex(state, _now);

        Assert.True(index >= 0);
        Assert.True(state.Programs[index].StartUtc <= _now);
        Assert.True(state.Programs[index].EndUtc > _now);
    }

    [Fact]
    public void Extend_WithNoContent_SchedulesNothing()
    {
        var state = new LiveChannelScheduleState();

        Assert.False(LiveChannelScheduler.Extend(state, _channelId, new Dictionary<string, LiveChannelUnit>(), _now, _now.AddHours(24)));
        Assert.Empty(state.Programs);
    }
}
