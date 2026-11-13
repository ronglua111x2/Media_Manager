using MediaManager.App.Tests.Cleanup;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.App.Tests.Link;

/// <summary>
/// Cross-review 2 (B, hunt side): the hunt and the cart never attach the torrent an episode, season pack or movie
/// was reset from, neither from search results nor as an "already in qBittorrent" copy.
/// </summary>
public sealed class ResetTorrentGuardTests : IDisposable
{
    private const string ResetHash = "4336f038aa14f56c4fffe29779f6d0b83c00c667";
    private readonly LinkTestHost _host = LinkTestHost.Create();

    public void Dispose() => _host.Dispose();

    [Fact]
    public void Guard_KnowsTheResetHashOfEachTargetKind()
    {
        var showId = _host.AddShow(5050, "Demo Show", (1, 1, "One"), (1, 2, "Two"));
        var episodeId = _host.Db.GetTrackedEpisodes(showId).Single(e => e.EpisodeNumber == 2).Id;
        _host.Db.AddTrackedEpisodeResetTorrentHash(episodeId, ResetHash);
        _host.Db.AddTrackedSeasonResetTorrentHash(showId, 1, "aaaabbbbccccddddeeeeffff0000111122223333");
        var movieId = _host.Db.UpsertTrackedMovie(new TrackedMovie { TmdbId = 9050, Title = "Demo Movie", ReleaseYear = 2016 });
        _host.Db.AddTrackedMovieResetTorrentHash(movieId, "1111222233334444555566667777888899990000");

        var episodeGuard = ResetTorrentGuard.For(new TorrentCartOrder { TargetKind = MediaKind.TvEpisode, MediaId = showId, EpisodeId = episodeId }, _host.Db);
        var packGuard = ResetTorrentGuard.For(new TorrentCartOrder { TargetKind = MediaKind.TvSeasonPack, MediaId = showId, SeasonNumber = 1 }, _host.Db);
        var movieGuard = ResetTorrentGuard.For(new TorrentCartOrder { TargetKind = MediaKind.Movie, MediaId = movieId }, _host.Db);

        Assert.True(episodeGuard(ResetHash.ToUpperInvariant()));
        Assert.False(episodeGuard("aaaabbbbccccddddeeeeffff0000111122223333"));
        Assert.True(packGuard("AAAABBBBCCCCDDDDEEEEFFFF0000111122223333"));
        Assert.True(movieGuard("1111222233334444555566667777888899990000"));
        Assert.False(movieGuard(null));
    }

    [Fact]
    public async Task Gate_RefusesAResetMagnet_WithoutAddingIt()
    {
        var qbittorrent = new FakeQbittorrentClient();
        var gate = CreateGate(qbittorrent);
        var order = Order($"magnet:?xt=urn:btih:{ResetHash}&dn=Show.S01E02");

        await Assert.ThrowsAsync<ResetTorrentReusedException>(
            () => gate.AddPausedValidateAndResumeAsync(order, @"C:\tmp", isResetHash: hash => string.Equals(hash, ResetHash, StringComparison.OrdinalIgnoreCase)));

        Assert.Equal(0, qbittorrent.AddCount);
        Assert.Equal(0, qbittorrent.ApplyManagedSettingsCount);
    }

    [Fact]
    public async Task Gate_RefusesTheExistingResetCopy_WithoutTouchingIt()
    {
        var qbittorrent = new FakeQbittorrentClient();
        var existing = new AddedTorrentResult { Hash = ResetHash, Name = "Show S01E02", State = "uploading" };
        qbittorrent.SeedExisting(existing);
        var gate = CreateGate(qbittorrent);

        await Assert.ThrowsAsync<ResetTorrentReusedException>(
            () => gate.AddPausedValidateAndResumeAsync(Order("https://example.test/b.torrent"), @"C:\tmp", existingTorrent: existing, isResetHash: hash => hash == ResetHash));

        Assert.Equal(0, qbittorrent.ApplyManagedSettingsCount);
        Assert.Equal(0, qbittorrent.DeleteCalls);
    }

    [Fact]
    public async Task Gate_DeletesANewlyAddedResetTorrent_WhoseHashWasOnlyKnownAfterAdd()
    {
        var qbittorrent = new FakeQbittorrentClient { Added = new AddedTorrentResult { Hash = ResetHash, Name = "Show S01E02", State = "downloading" } };
        var cleanup = new FakeCleanupService();
        var gate = CreateGate(qbittorrent, cleanup);
        string? reported = null;

        await Assert.ThrowsAsync<ResetTorrentReusedException>(
            () => gate.AddPausedValidateAndResumeAsync(Order("https://example.test/b.torrent"), @"C:\tmp", onTorrentAdded: t => reported = t.Hash, isResetHash: hash => hash == ResetHash));

        Assert.Equal(1, cleanup.DeleteCount);
        Assert.Null(reported);
    }

    private static TorrentAddGateService CreateGate(FakeQbittorrentClient qbittorrent, FakeCleanupService? cleanup = null) =>
        new(qbittorrent, new FakeSettingsService(), new FakeValidationService(), cleanup ?? new FakeCleanupService(), new FakeBlacklistService(), new FakeAppLogger());

    private static TorrentCartOrder Order(string url) => new()
    {
        Id = 1,
        Title = "Show S01E02",
        MediaId = 10,
        EpisodeId = 20,
        TargetKind = MediaKind.TvEpisode,
        SelectedCandidateUrl = url,
        SelectedCandidateName = "Show.S01E02.mkv"
    };
}
