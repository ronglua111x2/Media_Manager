using System.IO;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.App.Tests.Link;

/// <summary>
/// SEC-A01: reconcile adopts torrents it did not add (matched by name) only after the same content check and
/// blacklist the add gate uses. A rejected torrent is never deleted (it may be the user's own download, DEL-02):
/// it is left in qBittorrent, not attached, not linked, and reported.
/// </summary>
public sealed class AdoptSafetyTests : IDisposable
{
    private const string Name = "Demo.Show.S01E03.1080p.WEB-DL.AAC2.0.H.264-Grp";
    private const string BadHash = "bbbb0000000000000000000000000000000000c3";

    private readonly LinkTestHost _host = LinkTestHost.Create();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task UnsafeFiles_TorrentIsNotAdoptedLinkedOrDeleted()
    {
        _host.Settings.AutoTorrent.AutoLinkCompletedDownloads = true;
        var showId = _host.AddShow(5101, "Demo Show", (1, 3, "Three"));
        _host.AddNamedTorrent(BadHash, Name, $"{Name}/{Name}.mkv", $"{Name}/Setup.exe");
        _host.ContentVerdict = (hash, _) => Rejected(hash, "Setup.exe");

        var result = await _host.Reconciliation.ReconcileAsync(TorrentReconciliationScope.ForMedia(MediaKind.TvEpisode, showId));

        Assert.True(string.IsNullOrEmpty(_host.Db.GetTrackedEpisodes(showId).Single().TorrentHash));
        Assert.Empty(_host.Items());
        Assert.Empty(_host.LibraryFiles());
        Assert.Empty(_host.DeletedHashes);
        Assert.Contains(BadHash, _host.ValidatedHashes);
        Assert.Equal(1, result.UnsafeSkippedCount);
        Assert.Contains("Unsafe", result.Summary);
        Assert.Contains(_host.Warnings, warning => warning.Contains(BadHash, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GloballyBlacklistedHash_IsNotAdopted()
    {
        _host.Settings.AutoTorrent.AutoLinkCompletedDownloads = true;
        var showId = _host.AddShow(5102, "Demo Show", (1, 3, "Three"));
        _host.AddNamedTorrent(BadHash, Name, $"{Name}.mkv");
        await _host.Blacklist.AddToBlacklistAsync(999, "https://example.test/other", BadHash, "Malware: x", global: true);

        var result = await _host.Reconciliation.ReconcileAsync(TorrentReconciliationScope.ForMedia(MediaKind.TvEpisode, showId));

        Assert.True(string.IsNullOrEmpty(_host.Db.GetTrackedEpisodes(showId).Single().TorrentHash));
        Assert.Empty(_host.Items());
        Assert.Empty(_host.DeletedHashes);
        Assert.Equal(1, result.UnsafeSkippedCount);
    }

    [Fact]
    public async Task MetadataNotReady_IsNotAdoptedYetAndNotReportedUnsafe()
    {
        var showId = _host.AddShow(5103, "Demo Show", (1, 3, "Three"));
        _host.AddNamedTorrent(BadHash, Name, $"{Name}.mkv");
        _host.TorrentFiles[BadHash] = [];

        var result = await _host.Reconciliation.ReconcileAsync(TorrentReconciliationScope.ForMedia(MediaKind.TvEpisode, showId));

        Assert.True(string.IsNullOrEmpty(_host.Db.GetTrackedEpisodes(showId).Single().TorrentHash));
        Assert.Equal(0, result.UnsafeSkippedCount);
    }

    [Fact]
    public async Task SafeFiles_AreValidatedThenAdopted()
    {
        _host.Settings.AutoTorrent.AutoLinkCompletedDownloads = true;
        var showId = _host.AddShow(5104, "Demo Show", (1, 3, "Three"));
        _host.AddNamedTorrent("HSAFE", Name, $"{Name}.mkv");

        await _host.Reconciliation.ReconcileAsync(TorrentReconciliationScope.ForMedia(MediaKind.TvEpisode, showId));

        Assert.Contains("HSAFE", _host.ValidatedHashes);
        Assert.Equal("HSAFE", _host.Db.GetTrackedEpisodes(showId).Single().TorrentHash);
        Assert.Single(_host.Items());
    }

    private static TorrentContentValidationResult Rejected(string hash, string file)
    {
        var result = new TorrentContentValidationResult
        {
            TorrentHash = hash,
            IsValid = false,
            Recommendation = TorrentHandleRecommendation.Delete
        };
        result.SuspiciousFiles.Add(new SuspiciousFile
        {
            FileName = file,
            Extension = Path.GetExtension(file),
            Reason = "Dangerous extension",
            SuspicionLevel = SuspicionLevel.Critical
        });
        return result;
    }
}
