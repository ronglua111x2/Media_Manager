using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;
using Xunit;

namespace MediaManager.App.Tests.Services;

/// <summary>Audit 07d GOV-04: an episode whose hunt found nothing waits before the next try.</summary>
public sealed class AutoTrackHuntBackoffTests
{
    private static readonly DateTime NowUtc = new(2026, 11, 13, 6, 0, 0, DateTimeKind.Utc);

    private static TorrentCartOrder Order(TorrentOrderStatus status, TimeSpan ago, TorrentOrderSource source = TorrentOrderSource.AutoTrack) => new()
    {
        Status = status,
        Source = source,
        UpdatedUtc = NowUtc - ago
    };

    [Theory]
    [InlineData(2, 1)]      // aired 2 h ago: retry hourly
    [InlineData(30, 3)]     // aired yesterday: every 3 h
    [InlineData(100, 6)]    // aired 4 days ago: every 6 h
    [InlineData(400, 24)]   // aired weeks ago: daily
    public void RetryInterval_GrowsWithEpisodeAge(int airedHoursAgo, int expectedHours)
    {
        var nowLocal = NowUtc.ToLocalTime();

        Assert.Equal(TimeSpan.FromHours(expectedHours), AutoTrackHuntBackoff.RetryInterval(nowLocal.AddHours(-airedHoursAgo), nowLocal));
    }

    [Fact]
    public void NoCandidatesOrder_WaitsUntilTheInterval_ThenMayHuntAgain()
    {
        var airedLongAgo = NowUtc.ToLocalTime().AddDays(-30);

        Assert.NotNull(AutoTrackHuntBackoff.NextAttemptUtc(Order(TorrentOrderStatus.NoCandidates, TimeSpan.FromHours(2)), airedLongAgo, NowUtc));
        Assert.Null(AutoTrackHuntBackoff.NextAttemptUtc(Order(TorrentOrderStatus.NoCandidates, TimeSpan.FromHours(25)), airedLongAgo, NowUtc));
    }

    [Fact]
    public void OnlyAutoTrackNoCandidatesOrders_Wait()
    {
        var aired = NowUtc.ToLocalTime().AddDays(-30);

        Assert.Null(AutoTrackHuntBackoff.NextAttemptUtc(null, aired, NowUtc));
        Assert.Null(AutoTrackHuntBackoff.NextAttemptUtc(Order(TorrentOrderStatus.Failed, TimeSpan.FromMinutes(5)), aired, NowUtc));
        Assert.Null(AutoTrackHuntBackoff.NextAttemptUtc(Order(TorrentOrderStatus.NoCandidates, TimeSpan.FromMinutes(5), TorrentOrderSource.Manual), aired, NowUtc));
    }
}
