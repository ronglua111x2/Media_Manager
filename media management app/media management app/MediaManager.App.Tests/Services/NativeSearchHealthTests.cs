using media_management_app.Search;
using media_management_app.Services;
using Xunit;

namespace MediaManager.App.Tests.Services;

/// <summary>Plain-language site states shown in Settings and the search quota pill (Auto, News, Cart).</summary>
public sealed class NativeSearchHealthTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 3, 0, 0, TimeSpan.Zero);
    private static readonly IndexerPolicy Policy = new(6, 1, TimeSpan.FromSeconds(5), 1000);

    private static IndexerStateSnapshot State(int used, IndexerBreakerState breaker = IndexerBreakerState.Closed, DateTimeOffset? openUntil = null, DateTimeOffset? cooldown = null) => new()
    {
        IndexerId = "bitsearch",
        DayKey = "2026-11-13",
        RequestsToday = used,
        Breaker = breaker,
        OpenUntilUtc = openUntil,
        CooldownUntilUtc = cooldown
    };

    [Fact]
    public void DailyLimitReached_IsUnavailable()
    {
        var status = NativeSearchHealth.Describe(State(1000), Policy, enabled: true, pausedAll: false, Now);

        Assert.Equal(SearchSiteSeverity.Unavailable, status.Severity);
        Assert.StartsWith("Daily limit reached", status.Text);
    }

    [Fact]
    public void AutoTrackShareUsed_IsLimited()
    {
        var status = NativeSearchHealth.Describe(State(700), Policy, enabled: true, pausedAll: false, Now);

        Assert.Equal(SearchSiteSeverity.Limited, status.Severity);
        Assert.Contains("Auto-Track share used", status.Text);
    }

    [Fact]
    public void YesterdaysUsage_DoesNotCount()
    {
        var state = State(1000);
        state.DayKey = "2026-11-12";

        var status = NativeSearchHealth.Describe(state, Policy, enabled: true, pausedAll: false, Now);

        Assert.Equal(SearchSiteSeverity.Ok, status.Severity);
        Assert.Equal(0, status.UsedToday);
    }

    [Fact]
    public void OpenBreaker_PausedAll_AndOff_AreReported()
    {
        Assert.Equal(SearchSiteSeverity.Unavailable,
            NativeSearchHealth.Describe(State(5, IndexerBreakerState.Open, Now.AddMinutes(15)), Policy, true, false, Now).Severity);
        Assert.Equal(SearchSiteSeverity.Limited,
            NativeSearchHealth.Describe(State(5, cooldown: Now.AddSeconds(60)), Policy, true, false, Now).Severity);
        Assert.Equal(SearchSiteSeverity.Unavailable, NativeSearchHealth.Describe(State(5), Policy, true, pausedAll: true, Now).Severity);
        Assert.Equal(SearchSiteSeverity.Off, NativeSearchHealth.Describe(State(5), Policy, enabled: false, false, Now).Severity);
    }
}
