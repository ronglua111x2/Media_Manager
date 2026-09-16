using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.App.Tests.Cleanup;

public class TorrentAddGateCleanupTests
{
    [Fact]
    public async Task Add_MalwareDeleted_ThrowsMaliciousAndBlacklists()
    {
        var cleanup = new FakeCleanupService();
        var blacklist = new FakeBlacklistService();
        var validation = new FakeValidationService
        {
            Result = DeleteResult()
        };
        var sut = CreateGate(new FakeQbittorrentClient(), cleanup, blacklist, validation);

        var ex = await Assert.ThrowsAsync<MaliciousTorrentException>(
            () => sut.AddPausedValidateAndResumeAsync(CreateOrder(), @"C:\tmp"));

        Assert.Equal(1, cleanup.DeleteCount);
        Assert.Equal(1, blacklist.AddCount);
        Assert.Contains("FAILED", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Add_CleanupUnverified_ThrowsCleanupFailedAfterBlacklist()
    {
        var cleanup = new FakeCleanupService
        {
            DeleteException = new TorrentCleanupFailedException("abc123", "malware")
        };
        var blacklist = new FakeBlacklistService();
        var validation = new FakeValidationService
        {
            Result = DeleteResult()
        };
        var sut = CreateGate(new FakeQbittorrentClient(), cleanup, blacklist, validation);

        var ex = await Assert.ThrowsAsync<TorrentCleanupFailedException>(
            () => sut.AddPausedValidateAndResumeAsync(CreateOrder(), @"C:\tmp"));

        Assert.Equal("abc123", ex.TorrentHash);
        Assert.Equal(1, cleanup.DeleteCount);
        Assert.Equal(1, blacklist.AddCount);
    }

    [Fact]
    public async Task Add_ValidContent_DoesNotDelete()
    {
        var cleanup = new FakeCleanupService();
        var sut = CreateGate(new FakeQbittorrentClient(), cleanup, new FakeBlacklistService(), new FakeValidationService());

        var added = await sut.AddPausedValidateAndResumeAsync(CreateOrder(), @"C:\tmp");

        Assert.Equal("abc123", added.Hash);
        Assert.Equal(0, cleanup.DeleteCount);
    }

    [Fact]
    public async Task Add_DoesNotRequestPausedTorrent()
    {
        var qbittorrent = new FakeQbittorrentClient();
        var sut = CreateGate(qbittorrent, new FakeCleanupService(), new FakeBlacklistService(), new FakeValidationService());

        await sut.AddPausedValidateAndResumeAsync(CreateOrder(), @"C:\tmp");

        Assert.NotNull(qbittorrent.LastAdd);
        Assert.False(qbittorrent.LastAdd!.Paused);
    }

    [Fact]
    public void CleanupFailed_IsDistinctFromMaliciousReject()
    {
        Exception cleanup = new TorrentCleanupFailedException("abc123", "malware");
        Exception malicious = new MaliciousTorrentException("malware");

        Assert.False(cleanup is MaliciousTorrentException);
        Assert.True(ShouldHaltBatch(cleanup));
        Assert.False(ShouldHaltBatch(malicious));
    }

    private static bool ShouldHaltBatch(Exception ex) => ex is TorrentCleanupFailedException;

    private static TorrentAddGateService CreateGate(
        IQbittorrentClient qbittorrent,
        ITorrentCleanupService cleanup,
        ITorrentBlacklistService blacklist,
        ITorrentContentValidationService validation)
    {
        return new TorrentAddGateService(
            qbittorrent,
            new FakeSettingsService(),
            validation,
            cleanup,
            blacklist,
            new FakeAppLogger());
    }

    private static TorrentCartOrder CreateOrder()
    {
        return new TorrentCartOrder
        {
            Id = 1,
            Title = "Show S01E01",
            MediaId = 10,
            EpisodeId = 20,
            TargetKind = MediaKind.TvEpisode,
            SelectedCandidateUrl = "https://example.test/a.torrent",
            SelectedCandidateName = "Show.S01E01.mkv"
        };
    }

    private static TorrentContentValidationResult DeleteResult()
    {
        return new TorrentContentValidationResult
        {
            IsValid = false,
            TorrentHash = "abc123",
            Recommendation = TorrentHandleRecommendation.Delete,
            SuspiciousFiles =
            [
                new SuspiciousFile
                {
                    FileName = "payload.exe",
                    Extension = ".exe",
                    Reason = "dangerous extension",
                    SuspicionLevel = SuspicionLevel.Critical
                }
            ]
        };
    }
}
