using System.IO;
using MediaManager.App.Tests.Cleanup;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.App.Tests.Services;

/// <summary>
/// SEC-A07: a torrent the add gate found malicious is blocked for every show and movie, not only the one it was
/// first offered for. A manual "wrong content" reject stays per show.
/// </summary>
public sealed class TorrentBlacklistGlobalTests : IDisposable
{
    private const string Hash = "aaaa0000000000000000000000000000000000b1";
    private readonly string _stateFolder = Path.Combine(Path.GetTempPath(), "mm-blacklist-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void GlobalEntry_BlocksTheHashForAnotherShow()
    {
        var database = CreateDatabase();

        database.AddTorrentToBlacklist(1, "https://example.test/a", Hash, "Malware: x", isGlobal: true);

        Assert.True(database.IsTorrentBlacklisted(2, null, Hash));
        Assert.True(database.IsTorrentBlacklisted(1, null, Hash));
    }

    [Fact]
    public void PerShowEntry_DoesNotBlockAnotherShow()
    {
        var database = CreateDatabase();

        database.AddTorrentToBlacklist(1, "https://example.test/a", Hash, "Manual reject");

        Assert.True(database.IsTorrentBlacklisted(1, null, Hash));
        Assert.False(database.IsTorrentBlacklisted(2, null, Hash));
    }

    [Fact]
    public async Task Service_GlobalMalwareEntry_BlocksMagnetForAnotherMedia()
    {
        var service = new TorrentBlacklistService(CreateDatabase(), new FakeAppLogger());

        await service.AddToBlacklistAsync(1, "https://example.test/a", Hash, "Malware: x", global: true);

        Assert.True(service.IsBlacklisted(7, $"magnet:?xt=urn:btih:{Hash}&dn=Movie.NN"));
    }

    [Fact]
    public async Task Gate_MalwareReject_BlacklistsGlobally()
    {
        var blacklist = new FakeBlacklistService();
        var validation = new FakeValidationService
        {
            Result = new TorrentContentValidationResult
            {
                IsValid = false,
                TorrentHash = "abc123",
                Recommendation = TorrentHandleRecommendation.Delete
            }
        };
        var gate = new TorrentAddGateService(
            new FakeQbittorrentClient(),
            new FakeSettingsService(),
            validation,
            new FakeCleanupService(),
            blacklist,
            new FakeAppLogger());

        await Assert.ThrowsAsync<MaliciousTorrentException>(() => gate.AddPausedValidateAndResumeAsync(
            new TorrentCartOrder
            {
                MediaId = 1,
                TargetKind = MediaKind.TvEpisode,
                EpisodeId = 10,
                Title = "Show.NN S01E01",
                SelectedCandidateUrl = "https://example.test/listing",
                SelectedCandidateName = "Show.NN.S01E01"
            },
            @"C:\tmp"));

        Assert.Equal(new[] { true }, blacklist.GlobalFlags);
    }

    private DatabaseService CreateDatabase()
    {
        var database = new DatabaseService(new FakeAppLogger());
        database.Initialize(_stateFolder);
        return database;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_stateFolder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
