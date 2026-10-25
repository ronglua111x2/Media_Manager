using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.App.Tests.Services;

public class InterruptedOrderRecoveryTests
{
    private static readonly DateTime SessionStartUtc = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(TorrentOrderStatus.Searching)]
    [InlineData(TorrentOrderStatus.Approved)]
    public void IsInterrupted_StuckStatusFromPreviousSession_True(TorrentOrderStatus status)
    {
        var order = CreateOrder(status, updatedUtc: SessionStartUtc.AddMinutes(-5));

        Assert.True(InterruptedOrderRecovery.IsInterrupted(order, SessionStartUtc));
    }

    [Fact]
    public void IsInterrupted_UpdatedThisSession_False()
    {
        var order = CreateOrder(TorrentOrderStatus.Searching, updatedUtc: SessionStartUtc.AddSeconds(1));

        Assert.False(InterruptedOrderRecovery.IsInterrupted(order, SessionStartUtc));
    }

    [Theory]
    [InlineData(TorrentOrderStatus.Draft)]
    [InlineData(TorrentOrderStatus.CandidatesFound)]
    [InlineData(TorrentOrderStatus.NoCandidates)]
    [InlineData(TorrentOrderStatus.AddedToClient)]
    [InlineData(TorrentOrderStatus.Downloading)]
    [InlineData(TorrentOrderStatus.Completed)]
    [InlineData(TorrentOrderStatus.Failed)]
    public void IsInterrupted_OtherStatuses_False(TorrentOrderStatus status)
    {
        var order = CreateOrder(status, updatedUtc: SessionStartUtc.AddDays(-1));

        Assert.False(InterruptedOrderRecovery.IsInterrupted(order, SessionStartUtc));
    }

    [Fact]
    public void IsInterrupted_ManualOrder_False()
    {
        var order = CreateOrder(TorrentOrderStatus.Approved, updatedUtc: SessionStartUtc.AddDays(-1));
        order.Source = TorrentOrderSource.Manual;

        Assert.False(InterruptedOrderRecovery.IsInterrupted(order, SessionStartUtc));
    }

    [Fact]
    public void IsInterrupted_AlreadyHasTorrentHash_False()
    {
        // Reconcile owns orders whose hash was saved; only pre-hash interruptions are recovered here.
        var order = CreateOrder(TorrentOrderStatus.Approved, updatedUtc: SessionStartUtc.AddDays(-1));
        order.TorrentHash = "abc123";

        Assert.False(InterruptedOrderRecovery.IsInterrupted(order, SessionStartUtc));
    }

    [Fact]
    public void IsInterrupted_LocalTimeUpdatedUtc_ComparedInUtc()
    {
        var order = CreateOrder(
            TorrentOrderStatus.Searching,
            updatedUtc: SessionStartUtc.AddMinutes(-1).ToLocalTime());

        Assert.True(InterruptedOrderRecovery.IsInterrupted(order, SessionStartUtc));
    }

    [Fact]
    public void Decide_Searching_ResetsToDraft()
    {
        var order = CreateOrder(TorrentOrderStatus.Searching);

        Assert.Equal(
            InterruptedOrderAction.ResetToDraft,
            InterruptedOrderRecovery.Decide(order, liveTorrentFound: false, hasCandidates: true));
    }

    [Fact]
    public void Decide_ApprovedWithLiveTorrent_Revalidates()
    {
        var order = CreateOrder(TorrentOrderStatus.Approved, selectedUrl: "https://example.test/a.torrent");
        order.PendingValidationHash = "abc123";

        Assert.Equal(
            InterruptedOrderAction.RevalidateExisting,
            InterruptedOrderRecovery.Decide(order, liveTorrentFound: true, hasCandidates: true));
    }

    [Fact]
    public void Decide_ApprovedPendingHashGoneFromClient_ReturnsToCandidates()
    {
        var order = CreateOrder(TorrentOrderStatus.Approved, selectedUrl: "https://example.test/a.torrent");
        order.PendingValidationHash = "abc123";

        Assert.Equal(
            InterruptedOrderAction.ReturnToCandidates,
            InterruptedOrderRecovery.Decide(order, liveTorrentFound: false, hasCandidates: true));
    }

    [Fact]
    public void Decide_ApprovedNoTorrentNoCandidates_ResetsToDraft()
    {
        var order = CreateOrder(TorrentOrderStatus.Approved, selectedUrl: "https://example.test/a.torrent");

        Assert.Equal(
            InterruptedOrderAction.ResetToDraft,
            InterruptedOrderRecovery.Decide(order, liveTorrentFound: false, hasCandidates: false));
    }

    [Fact]
    public void Decide_ApprovedWithoutSelectedCandidate_ResetsToDraft()
    {
        var order = CreateOrder(TorrentOrderStatus.Approved, selectedUrl: string.Empty);

        Assert.Equal(
            InterruptedOrderAction.ResetToDraft,
            InterruptedOrderRecovery.Decide(order, liveTorrentFound: true, hasCandidates: true));
    }

    [Theory]
    [InlineData(TorrentOrderStatus.CandidatesFound)]
    [InlineData(TorrentOrderStatus.Downloading)]
    [InlineData(TorrentOrderStatus.Failed)]
    public void Decide_NonInterruptedStatus_None(TorrentOrderStatus status)
    {
        var order = CreateOrder(status, selectedUrl: "https://example.test/a.torrent");

        Assert.Equal(
            InterruptedOrderAction.None,
            InterruptedOrderRecovery.Decide(order, liveTorrentFound: true, hasCandidates: true));
    }

    [Fact]
    public void CollectPendingValidationHashes_SkipsEmptyAndIgnoresCase()
    {
        var orders = new[]
        {
            new TorrentCartOrder { PendingValidationHash = "ABC123" },
            new TorrentCartOrder { PendingValidationHash = string.Empty },
            new TorrentCartOrder { PendingValidationHash = "  " },
            new TorrentCartOrder { PendingValidationHash = "def456" }
        };

        var hashes = InterruptedOrderRecovery.CollectPendingValidationHashes(orders);

        Assert.Equal(2, hashes.Count);
        Assert.Contains("abc123", hashes);
        Assert.Contains("DEF456", hashes);
    }

    private static TorrentCartOrder CreateOrder(
        TorrentOrderStatus status,
        DateTime? updatedUtc = null,
        string selectedUrl = "https://example.test/a.torrent")
    {
        return new TorrentCartOrder
        {
            Id = 1,
            Title = "S01E01 - Pilot",
            TargetKind = MediaKind.TvEpisode,
            MediaId = 10,
            EpisodeId = 20,
            Source = TorrentOrderSource.AutoTrack,
            Status = status,
            SelectedCandidateUrl = selectedUrl,
            UpdatedUtc = updatedUtc ?? SessionStartUtc.AddHours(-1)
        };
    }
}
