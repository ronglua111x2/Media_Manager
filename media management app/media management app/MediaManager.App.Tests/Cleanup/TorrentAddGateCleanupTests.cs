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
    public async Task TryGetExisting_MagnetHashAlreadyListed_ReturnsTorrent()
    {
        var qbittorrent = new FakeQbittorrentClient();
        qbittorrent.SeedExisting(new AddedTorrentResult
        {
            Hash = ExistingHash,
            Name = "www.UIndex.org - Show S01E01",
            State = "pausedDL"
        });
        var sut = CreateGate(qbittorrent, new FakeCleanupService(), new FakeBlacklistService(), new FakeValidationService());

        var existing = await sut.TryGetExistingByListingUrlAsync(MagnetUrl);

        Assert.NotNull(existing);
        Assert.Equal(ExistingHash, existing!.Hash, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("www.UIndex.org - Show S01E01", existing.Name);
    }

    [Fact]
    public async Task TryGetExisting_HttpListing_ReturnsNull()
    {
        var sut = CreateGate(new FakeQbittorrentClient(), new FakeCleanupService(), new FakeBlacklistService(), new FakeValidationService());

        var existing = await sut.TryGetExistingByListingUrlAsync("https://example.test/a.torrent");

        Assert.Null(existing);
    }

    [Fact]
    public async Task Add_ExistingTorrent_SkipsAddAndAppliesManagedSettings()
    {
        var qbittorrent = new FakeQbittorrentClient();
        var existing = new AddedTorrentResult
        {
            Hash = ExistingHash,
            Name = "www.UIndex.org - Show S01E01",
            State = "pausedDL"
        };
        qbittorrent.SeedExisting(existing);
        var cleanup = new FakeCleanupService();
        var sut = CreateGate(qbittorrent, cleanup, new FakeBlacklistService(), new FakeValidationService());

        var added = await sut.AddPausedValidateAndResumeAsync(
            CreateMagnetOrder(),
            @"C:\tmp",
            existingTorrent: existing);

        Assert.Equal(ExistingHash, added.Hash, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(0, qbittorrent.AddCount);
        Assert.Equal(1, qbittorrent.ApplyManagedSettingsCount);
        Assert.Equal(0, cleanup.DeleteCount);
        Assert.Null(qbittorrent.LastAdd);
    }

    [Fact]
    public async Task Add_ExistingBlacklistedHash_ThrowsMaliciousWithoutAdd()
    {
        var qbittorrent = new FakeQbittorrentClient();
        var existing = new AddedTorrentResult
        {
            Hash = ExistingHash,
            Name = "www.UIndex.org - Show S01E01",
            State = "pausedDL"
        };
        qbittorrent.SeedExisting(existing);
        var cleanup = new FakeCleanupService();
        var blacklist = new FakeBlacklistService { InfoHashBlacklisted = true };
        var sut = CreateGate(qbittorrent, cleanup, blacklist, new FakeValidationService());

        await Assert.ThrowsAsync<MaliciousTorrentException>(
            () => sut.AddPausedValidateAndResumeAsync(
                CreateMagnetOrder(),
                @"C:\tmp",
                existingTorrent: existing));

        Assert.Equal(0, qbittorrent.AddCount);
        Assert.Equal(1, cleanup.DeleteCount);
        Assert.Equal(1, blacklist.AddCount);
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

    private static TorrentCartOrder CreateMagnetOrder()
    {
        return new TorrentCartOrder
        {
            Id = 1,
            Title = "Show S01E01",
            MediaId = 10,
            EpisodeId = 20,
            TargetKind = MediaKind.TvEpisode,
            SelectedCandidateUrl = MagnetUrl,
            SelectedCandidateName = "Show.S01E01.mkv"
        };
    }

    private const string ExistingHash = "4336f038aa14f56c4fffe29779f6d0b83c00c667";

    private const string MagnetUrl =
        "magnet:?xt=urn:btih:4336F038AA14F56C4FFFE29779F6D0B83C00C667&dn=Show";

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
