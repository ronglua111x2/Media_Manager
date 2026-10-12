using media_management_app.Common;
using media_management_app.Services;

namespace MediaManager.App.Tests.Cleanup;

public class TorrentCleanupServiceTests
{
    [Fact]
    public async Task DeleteTorrentAsync_VerifiedRemoval_DoesNotPause()
    {
        var qbittorrent = new FakeQbittorrentClient();
        var sut = new TorrentCleanupService(qbittorrent, new FakeAppLogger());

        var deleted = await sut.DeleteTorrentAsync("abc123", reason: "malware");

        Assert.True(deleted);
        Assert.Equal(1, qbittorrent.DeleteCalls);
        Assert.Equal(0, qbittorrent.PauseCalls);
    }

    [Fact]
    public async Task DeleteTorrentAsync_StillListed_RetriesThenPausesAndThrows()
    {
        var qbittorrent = new FakeQbittorrentClient { RemainAfterDelete = true };
        var sut = new TorrentCleanupService(qbittorrent, new FakeAppLogger());

        var ex = await Assert.ThrowsAsync<TorrentCleanupFailedException>(
            () => sut.DeleteTorrentAsync("abc123", reason: "malware"));

        Assert.Equal("abc123", ex.TorrentHash);
        Assert.Equal("malware", ex.CleanupReason);
        Assert.Contains("abc123", ex.OrderStatusDetail, StringComparison.Ordinal);
        Assert.Equal(TorrentCleanupService.MaxDeleteAttempts, qbittorrent.DeleteCalls);
        Assert.Equal(1, qbittorrent.PauseCalls);
    }

    [Fact]
    public async Task DeleteTorrentAsync_TransportErrors_RetryThenPauseAndThrow()
    {
        var qbittorrent = new FakeQbittorrentClient
        {
            DeleteException = new InvalidOperationException("Failed to delete torrents from qBittorrent: 500")
        };
        var sut = new TorrentCleanupService(qbittorrent, new FakeAppLogger());

        var ex = await Assert.ThrowsAsync<TorrentCleanupFailedException>(
            () => sut.DeleteTorrentAsync("abc123", reason: "blacklisted infohash"));

        Assert.Equal("abc123", ex.TorrentHash);
        Assert.NotNull(ex.InnerException);
        Assert.Equal(TorrentCleanupService.MaxDeleteAttempts, qbittorrent.DeleteCalls);
        Assert.Equal(1, qbittorrent.PauseCalls);
    }

    [Fact]
    public async Task DeleteTorrentAsync_SucceedsOnLaterAttempt_DoesNotThrow()
    {
        var qbittorrent = new FakeQbittorrentClient { DeletesBeforeRemoval = 2 };
        var sut = new TorrentCleanupService(qbittorrent, new FakeAppLogger());

        var deleted = await sut.DeleteTorrentAsync("abc123", reason: "malware");

        Assert.True(deleted);
        Assert.Equal(3, qbittorrent.DeleteCalls);
        Assert.Equal(0, qbittorrent.PauseCalls);
    }
}
