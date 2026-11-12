using FluentAssertions;
using media_management_app.Search;

namespace MediaManager.Core.Tests.Search;

/// <summary>native-search P2: request governor (docs/planning/native-search/03-request-governor.md).</summary>
public class IndexerRequestGovernorTests
{
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;

        public void Advance(TimeSpan by) => Now += by;
    }

    private static readonly IndexerPolicy Tight = new(RequestsPerMinute: 2, MaxConcurrent: 1, MinInterval: TimeSpan.Zero, DailyBudget: 10);

    private static (IndexerRequestGovernor Governor, ManualTime Time) Create(IndexerPolicy? policy = null)
    {
        var time = new ManualTime();
        return (new IndexerRequestGovernor(time, _ => policy ?? Tight), time);
    }

    private static async Task Request(IndexerRequestGovernor governor, string id = "site", SearchLane lane = SearchLane.Interactive)
    {
        using var lease = await governor.AcquireAsync(id, lane, TimeSpan.Zero, CancellationToken.None);
        governor.Report(id, IndexerFailureKind.None);
    }

    [Fact]
    public async Task RequestsPerMinute_RefusesBeyondLimitWithinTheWindow()
    {
        var (governor, time) = Create();
        await Request(governor);
        await Request(governor);

        var third = () => governor.AcquireAsync("site", SearchLane.Interactive, TimeSpan.Zero, CancellationToken.None);
        await third.Should().ThrowAsync<IndexerUnavailableException>();

        time.Advance(TimeSpan.FromMinutes(1));
        await Request(governor);
    }

    [Fact]
    public async Task MaxConcurrent_QueuesWhileALeaseIsHeld_ButSiteStaysAvailable()
    {
        var (governor, _) = Create(Tight with { RequestsPerMinute = 100 });
        using var held = await governor.AcquireAsync("site", SearchLane.Interactive, TimeSpan.Zero, CancellationToken.None);

        // A temporary wait does not make the site unavailable (searches queue instead of skipping it)...
        governor.GetAvailability("site").CanRun.Should().BeTrue();
        // ...but a request that cannot wait is refused.
        var now = () => governor.AcquireAsync("site", SearchLane.Interactive, TimeSpan.Zero, CancellationToken.None);
        await now.Should().ThrowAsync<IndexerUnavailableException>();
        held.Dispose();
    }

    [Fact]
    public async Task DailyBudget_BackgroundLaneGetsOnlyItsShare()
    {
        var (governor, time) = Create(new IndexerPolicy(1000, 5, TimeSpan.Zero, DailyBudget: 10, BackgroundBudgetShare: 0.7));
        for (var i = 0; i < 7; i++)
        {
            await Request(governor, lane: SearchLane.Background);
        }

        governor.GetAvailability("site", SearchLane.Background).CanRun.Should().BeFalse();
        governor.GetAvailability("site", SearchLane.Interactive).CanRun.Should().BeTrue();

        for (var i = 0; i < 3; i++)
        {
            await Request(governor);
        }

        governor.GetAvailability("site", SearchLane.Interactive).Reason.Should().Contain("daily budget");

        time.Advance(TimeSpan.FromDays(1));
        governor.GetAvailability("site", SearchLane.Background).CanRun.Should().BeTrue();
    }

    [Fact]
    public async Task RateLimited_HonoursRetryAfterAsCooldown()
    {
        var (governor, time) = Create(Tight with { RequestsPerMinute = 100 });
        using (await governor.AcquireAsync("site", SearchLane.Interactive, TimeSpan.Zero, CancellationToken.None))
        {
            governor.Report("site", IndexerFailureKind.RateLimited, TimeSpan.FromSeconds(120), "HTTP 429");
        }

        governor.GetAvailability("site").Reason.Should().Contain("cooling down");
        time.Advance(TimeSpan.FromSeconds(121));
        governor.GetAvailability("site").CanRun.Should().BeTrue();
    }

    [Fact]
    public async Task Blocked_OpensBreaker_WithEscalatingDuration()
    {
        var (governor, time) = Create(Tight with { RequestsPerMinute = 100 });
        using (await governor.AcquireAsync("site", SearchLane.Interactive, TimeSpan.Zero, CancellationToken.None))
        {
            governor.Report("site", IndexerFailureKind.Blocked, detail: "HTTP 403");
        }

        governor.GetAvailability("site").Reason.Should().Contain("breaker open");
        time.Advance(TimeSpan.FromMinutes(16));

        // Half-open probe fails again: the breaker reopens for longer (1 h).
        using (await governor.AcquireAsync("site", SearchLane.Interactive, TimeSpan.Zero, CancellationToken.None))
        {
            governor.Report("site", IndexerFailureKind.Blocked, detail: "HTTP 403");
        }

        time.Advance(TimeSpan.FromMinutes(30));
        governor.GetAvailability("site").CanRun.Should().BeFalse();
        time.Advance(TimeSpan.FromMinutes(31));
        governor.GetAvailability("site").CanRun.Should().BeTrue();
    }

    [Fact]
    public async Task RepeatedFailures_OpenBreaker_AndSuccessClosesIt()
    {
        var (governor, time) = Create(Tight with { RequestsPerMinute = 100 });
        for (var i = 0; i < 3; i++)
        {
            using (await governor.AcquireAsync("site", SearchLane.Interactive, TimeSpan.Zero, CancellationToken.None))
            {
                governor.Report("site", IndexerFailureKind.Timeout, detail: "timeout");
            }
        }

        governor.GetAvailability("site").CanRun.Should().BeFalse();
        time.Advance(TimeSpan.FromMinutes(6));
        await Request(governor);
        governor.Snapshot().Single().Breaker.Should().Be(IndexerBreakerState.Closed);
    }

    [Fact]
    public async Task KillSwitch_StopsEverything()
    {
        var (governor, _) = Create();
        governor.KillSwitch = true;

        var acquire = () => governor.AcquireAsync("site", SearchLane.Interactive, TimeSpan.FromSeconds(5), CancellationToken.None);
        await acquire.Should().ThrowAsync<IndexerUnavailableException>().Where(ex => ex.Reason.Contains("kill switch"));
    }

    [Fact]
    public async Task State_SurvivesRestart()
    {
        var (governor, time) = Create(Tight with { RequestsPerMinute = 100 });
        using (await governor.AcquireAsync("site", SearchLane.Interactive, TimeSpan.Zero, CancellationToken.None))
        {
            governor.Report("site", IndexerFailureKind.Blocked, detail: "HTTP 403");
        }

        var restarted = new IndexerRequestGovernor(time, _ => Tight, governor.Snapshot());

        restarted.GetAvailability("site").CanRun.Should().BeFalse();
        restarted.Snapshot().Single().RequestsToday.Should().Be(1);
    }

    [Fact]
    public void Policy_UserSettingsCanOnlyTighten()
    {
        var policy = IndexerPolicy.DefaultFor("knaben");

        policy.RestrictTo(requestsPerMinute: 1000, dailyBudget: 100000).Should().Be(policy);
        policy.RestrictTo(requestsPerMinute: 3, dailyBudget: 50).Should().Be(policy with { RequestsPerMinute = 3, DailyBudget = 50 });
    }
}
