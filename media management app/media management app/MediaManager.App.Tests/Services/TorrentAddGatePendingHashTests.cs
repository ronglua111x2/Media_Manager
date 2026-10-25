using MediaManager.App.Tests.Cleanup;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.App.Tests.Services;

public class TorrentAddGatePendingHashTests
{
    private const string ExistingHash = "4336f038aa14f56c4fffe29779f6d0b83c00c667";

    [Fact]
    public async Task Add_InvokesOnTorrentAddedWithHashBeforeValidation()
    {
        var events = new List<string>();
        var validation = new FakeValidationService { OnValidateFiles = () => events.Add("validate") };
        var sut = CreateGate(new FakeQbittorrentClient(), new FakeCleanupService(), validation);

        await sut.AddPausedValidateAndResumeAsync(
            CreateOrder(),
            @"C:\tmp",
            onTorrentAdded: torrent => events.Add($"added:{torrent.Hash}"));

        Assert.Equal(new[] { "added:abc123", "validate" }, events);
    }

    [Fact]
    public async Task Add_MalwareRejected_HashWasStillReportedFirst()
    {
        string? reported = null;
        var validation = new FakeValidationService
        {
            Result = new TorrentContentValidationResult
            {
                IsValid = false,
                TorrentHash = "abc123",
                Recommendation = TorrentHandleRecommendation.Delete
            }
        };
        var sut = CreateGate(new FakeQbittorrentClient(), new FakeCleanupService(), validation);

        await Assert.ThrowsAsync<MaliciousTorrentException>(
            () => sut.AddPausedValidateAndResumeAsync(
                CreateOrder(),
                @"C:\tmp",
                onTorrentAdded: torrent => reported = torrent.Hash));

        Assert.Equal("abc123", reported);
    }

    [Fact]
    public async Task Add_ExistingTorrent_ReportsExistingHash()
    {
        var qbittorrent = new FakeQbittorrentClient();
        var existing = new AddedTorrentResult { Hash = ExistingHash, Name = "Show S01E01", State = "downloading" };
        qbittorrent.SeedExisting(existing);
        string? reported = null;
        var sut = CreateGate(qbittorrent, new FakeCleanupService(), new FakeValidationService());

        await sut.AddPausedValidateAndResumeAsync(
            CreateOrder(),
            @"C:\tmp",
            existingTorrent: existing,
            onTorrentAdded: torrent => reported = torrent.Hash);

        Assert.Equal(ExistingHash, reported);
    }

    [Fact]
    public async Task TryGetExistingByHash_ListedHash_ReturnsTorrent()
    {
        var qbittorrent = new FakeQbittorrentClient();
        qbittorrent.SeedExisting(new AddedTorrentResult { Hash = ExistingHash, Name = "Show S01E01" });
        var sut = CreateGate(qbittorrent, new FakeCleanupService(), new FakeValidationService());

        var found = await sut.TryGetExistingByHashAsync(ExistingHash.ToUpperInvariant());

        Assert.NotNull(found);
        Assert.Equal(ExistingHash, found!.Hash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0000000000000000000000000000000000000000")]
    public async Task TryGetExistingByHash_EmptyOrUnknown_ReturnsNull(string? hash)
    {
        var qbittorrent = new FakeQbittorrentClient();
        qbittorrent.SeedExisting(new AddedTorrentResult { Hash = ExistingHash, Name = "Show S01E01" });
        var sut = CreateGate(qbittorrent, new FakeCleanupService(), new FakeValidationService());

        Assert.Null(await sut.TryGetExistingByHashAsync(hash));
    }

    private static TorrentAddGateService CreateGate(
        IQbittorrentClient qbittorrent,
        ITorrentCleanupService cleanup,
        ITorrentContentValidationService validation)
    {
        return new TorrentAddGateService(
            qbittorrent,
            new FakeSettingsService(),
            validation,
            cleanup,
            new FakeBlacklistService(),
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
}
