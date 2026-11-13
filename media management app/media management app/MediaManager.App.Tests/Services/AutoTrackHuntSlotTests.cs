using media_management_app.Services;
using Xunit;

namespace MediaManager.App.Tests.Services;

/// <summary>
/// GOV-04 hunt interval (main) meets ForceTunnelForHunt (PR #12): a hunt aborted in preflight because no WARP
/// tunnel was available searched nothing, so the next 30-minute cycle may try again.
/// </summary>
public sealed class AutoTrackHuntSlotTests
{
    private static readonly DateTime T0 = new(2026, 11, 13, 6, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(60);

    [Fact]
    public void FirstHunt_ClaimsTheSlot_NextCycleWaitsForTheInterval()
    {
        var slot = new AutoTrackHuntSlot();

        Assert.Null(slot.TryClaim(T0, Interval));
        Assert.Equal(T0 + Interval, slot.TryClaim(T0.AddMinutes(30), Interval));
        Assert.Null(slot.TryClaim(T0.AddMinutes(59), Interval)); // one minute of slack for timer drift
    }

    [Fact]
    public void PreflightAbort_GivesTheSlotBack_SoTheNextCycleRetries()
    {
        var slot = new AutoTrackHuntSlot();

        Assert.Null(slot.TryClaim(T0, Interval));
        slot.GiveBack();

        Assert.Null(slot.TryClaim(T0.AddMinutes(30), Interval));
    }

    [Fact]
    public void GiveBack_RestoresThePreviousHunt_NotAnEmptySlot()
    {
        var slot = new AutoTrackHuntSlot();
        Assert.Null(slot.TryClaim(T0, Interval));               // hunt ran
        Assert.Null(slot.TryClaim(T0.AddMinutes(60), Interval)); // next hunt claimed, then aborted
        slot.GiveBack();

        // The completed hunt at T0 still counts: 60 minutes after it, a new claim is allowed...
        Assert.Null(slot.TryClaim(T0.AddMinutes(90), Interval));
        // ...and a successful hunt keeps the slot until the interval passes.
        Assert.Equal(T0.AddMinutes(150), slot.TryClaim(T0.AddMinutes(120), Interval));
    }
}
