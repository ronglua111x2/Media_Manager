using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.App.Tests.Services;

public class AutoTrackAnchorCycleTests
{
    // Sunday 2026-10-25 22:39 — the run that skipped A Certain Dark Item in the live log.
    private static readonly DateTime SundayNight = new(2026, 10, 25, 22, 39, 0);

    private static readonly AutoTrackSettings Settings = new()
    {
        AnchorDayOfWeek = DayOfWeek.Sunday,
        AnchorTimeLocal = "18:00",
        EnforceGlobalWeeklySchedule = true
    };

    private static TrackedShow Show(DayOfWeek day, string time, DateTime? lastRefresh) => new()
    {
        Id = 1,
        Title = "Test Show",
        AutoTrackFromSeason = 1,
        AutoTrackFromEpisode = 1,
        AutoTrackAnchorDayOfWeek = day,
        AutoTrackAnchorTimeLocal = time,
        AutoTrackLastTmdbRefreshLocal = lastRefresh
    };

    [Fact]
    public void CycleAnchor_SundayWithSaturdayAnchor_IsPreviousSaturday()
    {
        var anchor = AutoTrackWeekAnchor.GetCurrentCycleAnchor(DayOfWeek.Saturday, new TimeSpan(6, 0, 0), SundayNight);

        Assert.Equal(new DateTime(2026, 10, 24, 6, 0, 0), anchor);
    }

    [Fact]
    public void NextCycleAnchor_IsSevenDaysAfterCycleAnchor()
    {
        var show = Show(DayOfWeek.Saturday, "06:00", null);

        Assert.Equal(new DateTime(2026, 10, 31, 6, 0, 0), AutoTrackWeekAnchor.GetNextCycleAnchor(show, SundayNight, Settings));
    }

    [Fact]
    public void CycleAnchor_ExactlyAtAnchor_IsThatAnchor()
    {
        var now = new DateTime(2026, 10, 27, 8, 30, 0);

        Assert.Equal(now, AutoTrackWeekAnchor.GetCurrentCycleAnchor(DayOfWeek.Tuesday, new TimeSpan(8, 30, 0), now));
    }

    [Fact]
    public void CycleAnchor_SameDayBeforeAnchorTime_IsPreviousWeek()
    {
        var now = new DateTime(2026, 10, 27, 8, 29, 0);

        Assert.Equal(
            new DateTime(2026, 10, 20, 8, 30, 0),
            AutoTrackWeekAnchor.GetCurrentCycleAnchor(DayOfWeek.Tuesday, new TimeSpan(8, 30, 0), now));
    }

    [Fact]
    public void ShouldRefresh_MissedSaturdayAnchorAcrossWeekBoundary_IsDue()
    {
        // A Certain Dark Item: last refresh Sat 10-17, anchor Sat 10-24 06:00 missed while the app was closed.
        var show = Show(DayOfWeek.Saturday, "06:00", new DateTime(2026, 10, 17, 21, 4, 0));

        var due = AutoTrackTmdbEligibility.ShouldRefreshTmdb(show, Settings, [], SundayNight, out var reason);

        Assert.True(due, reason);
    }

    [Fact]
    public void ShouldRefresh_RefreshedAfterCurrentCycleAnchor_IsSkipped()
    {
        // From Far Away: anchor Tue 08:30, refreshed Tue 10-20 19:13; next cycle starts Tue 10-27 08:30.
        var show = Show(DayOfWeek.Tuesday, "08:30", new DateTime(2026, 10, 20, 19, 13, 0));

        var due = AutoTrackTmdbEligibility.ShouldRefreshTmdb(show, Settings, [], SundayNight, out var reason);

        Assert.False(due);
        Assert.Contains("current cycle", reason);
    }

    [Fact]
    public void ShouldRefresh_NeverRefreshed_IsDue()
    {
        var show = Show(DayOfWeek.Saturday, "06:00", null);

        Assert.True(AutoTrackTmdbEligibility.ShouldRefreshTmdb(show, Settings, [], SundayNight, out _));
    }

    [Fact]
    public void ShouldRefresh_ScheduleNotEnforced_IsAlwaysDue()
    {
        var settings = new AutoTrackSettings { EnforceGlobalWeeklySchedule = false };
        var show = new TrackedShow
        {
            Id = 1,
            AutoTrackFromSeason = 1,
            AutoTrackFromEpisode = 1,
            AutoTrackLastTmdbRefreshLocal = SundayNight.AddMinutes(-1)
        };

        Assert.True(AutoTrackTmdbEligibility.ShouldRefreshTmdb(show, settings, [], SundayNight, out _));
    }

    [Fact]
    public void ShouldRefresh_BypassAnchor_IgnoresSatisfiedCycle()
    {
        var show = Show(DayOfWeek.Tuesday, "08:30", new DateTime(2026, 10, 20, 19, 13, 0));

        Assert.True(AutoTrackTmdbEligibility.ShouldRefreshTmdb(show, Settings, [], SundayNight, out _, bypassAnchor: true));
    }

    [Fact]
    public void DormantReset_AfterNewCycleAnchor_ResetsOnlyWhenUnsatisfied()
    {
        var stale = Show(DayOfWeek.Saturday, "06:00", new DateTime(2026, 10, 17, 21, 4, 0));
        stale.AutoTrackTmdbState = AutoTrackTmdbState.DormantCaughtUp;
        var fresh = Show(DayOfWeek.Saturday, "06:00", new DateTime(2026, 10, 24, 7, 0, 0));
        fresh.AutoTrackTmdbState = AutoTrackTmdbState.DormantCaughtUp;

        Assert.True(AutoTrackTmdbEligibility.ShouldResetDormantState(stale, Settings, SundayNight));
        Assert.False(AutoTrackTmdbEligibility.ShouldResetDormantState(fresh, Settings, SundayNight));
    }

    [Fact]
    public void FilterToCycle_KeepsEpisodesAiredByCycleAnchor()
    {
        var cycleAnchor = new DateTime(2026, 10, 24, 6, 0, 0);
        TrackedEpisode[] pending =
        [
            new() { Id = 1, SeasonNumber = 1, EpisodeNumber = 3, AirDate = new DateTime(2026, 10, 23) },
            new() { Id = 2, SeasonNumber = 1, EpisodeNumber = 4, AirDate = new DateTime(2026, 10, 30) },
            new() { Id = 3, SeasonNumber = 1, EpisodeNumber = 5, AirDate = null }
        ];

        var kept = AutoTrackTmdbEligibility.FilterToCycle(pending, cycleAnchor);

        Assert.Equal([1L, 3L], kept.Select(episode => episode.Id));
    }
}
