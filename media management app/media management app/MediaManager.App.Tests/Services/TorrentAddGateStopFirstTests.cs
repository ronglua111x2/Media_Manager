using System.Net.Http;
using MediaManager.App.Tests.Cleanup;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.App.Tests.Services;

/// <summary>
/// S0 (SEC-A03, SEC-A02): a torrent is checked before it downloads its payload, extras outside the allow-list are never
/// downloaded, and an add that ends early leaves the torrent stopped instead of downloading unchecked.
/// </summary>
public class TorrentAddGateStopFirstTests
{
    private const string Magnet = "magnet:?xt=urn:btih:cccc0000000000000000000000000000000000d4&dn=Show.NN.S01E01";

    [Fact]
    public async Task FetchedMetadata_Malware_IsRejectedWithoutAddingAnything()
    {
        var qbittorrent = new FakeQbittorrentClient
        {
            WebApiVersion = new Version(2, 15, 1),
            Inspection = Inspection(File("Show.NN.S01E01.1080p.mkv", 900_000_000), File("Setup.exe", 3_000_000))
        };
        var blacklist = new FakeBlacklistService();
        var gate = CreateGate(qbittorrent, blacklist);

        await Assert.ThrowsAsync<MaliciousTorrentException>(() => gate.AddPausedValidateAndResumeAsync(Order(Magnet), @"C:\tmp"));

        Assert.Equal(0, qbittorrent.AddCount);
        Assert.Equal(0, qbittorrent.DeleteCalls);
        Assert.Equal(new[] { true }, blacklist.GlobalFlags);
        Assert.True(qbittorrent.LastInspectFetchMetadata);
    }

    [Fact]
    public async Task FetchedMetadata_Clean_AddsWithUrlAndExtrasAtPriorityZero()
    {
        var qbittorrent = new FakeQbittorrentClient
        {
            WebApiVersion = new Version(2, 15, 1),
            Inspection = Inspection(
                File("Show.NN.S01E01.1080p/Show.NN.S01E01.1080p.mkv", 900_000_000),
                File("Show.NN.S01E01.1080p/Visit Our Site.url", 120),
                File("Show.NN.S01E01.1080p/Show.NN.S01E01.en.srt", 40_000))
        };
        var gate = CreateGate(qbittorrent, new FakeBlacklistService());

        await gate.AddPausedValidateAndResumeAsync(Order(Magnet), @"C:\tmp");

        Assert.Equal(1, qbittorrent.AddCount);
        Assert.Equal(new[] { 1, 0, 1 }, qbittorrent.LastAdd!.FilePriorities);
        Assert.False(qbittorrent.LastAdd.StopAfterMetadata);
    }

    [Fact]
    public async Task TorrentFile_IsCheckedBeforeAddAndTheSameBytesAreAddedStopped()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var qbittorrent = new FakeQbittorrentClient
        {
            WebApiVersion = new Version(2, 11, 4),
            Inspection = Inspection(File("Show.NN.S01E01.1080p.mkv", 900_000_000)) with { TorrentBytes = bytes, AddUrl = "https://example.test/x.torrent" }
        };
        qbittorrent.Files = [new TorrentContentFile { Name = "Show.NN.S01E01.1080p.mkv", Size = 900_000_000 }, new TorrentContentFile { Name = "Site.url", Size = 120 }];
        var gate = CreateGate(qbittorrent, new FakeBlacklistService());

        await gate.AddPausedValidateAndResumeAsync(Order("https://example.test/listing"), @"C:\tmp");

        Assert.Same(bytes, qbittorrent.LastAdd!.TorrentBytes);
        Assert.True(qbittorrent.LastAdd.Paused);
        Assert.Contains(qbittorrent.PriorityCalls, call => call.Priority == 0 && call.Indexes.SequenceEqual(new[] { 1 }));
        Assert.Equal(1, qbittorrent.ResumeCalls);
    }

    [Fact]
    public async Task NoPreAddCheck_AddsWithStopConditionThenStartsAfterValidation()
    {
        var qbittorrent = new FakeQbittorrentClient { WebApiVersion = new Version(2, 11, 4) };
        qbittorrent.Files = [new TorrentContentFile { Name = "Show.NN.S01E01.mkv", Size = 900_000_000 }, new TorrentContentFile { Name = "Site.url", Size = 120 }];
        var events = new List<string>();
        qbittorrent.OnEvent = events.Add;
        var gate = CreateGate(qbittorrent, new FakeBlacklistService());

        await gate.AddPausedValidateAndResumeAsync(Order(Magnet), @"C:\tmp");

        Assert.True(qbittorrent.LastAdd!.StopAfterMetadata);
        Assert.Equal(new[] { "add", "files", "priority", "resume" }, events);
    }

    [Fact]
    public async Task OldQbittorrent_KeepsTheRunningAdd()
    {
        var qbittorrent = new FakeQbittorrentClient { WebApiVersion = new Version(2, 8, 5) };
        var gate = CreateGate(qbittorrent, new FakeBlacklistService());

        await gate.AddPausedValidateAndResumeAsync(Order(Magnet), @"C:\tmp");

        Assert.False(qbittorrent.LastAdd!.StopAfterMetadata);
        Assert.False(qbittorrent.LastAdd.Paused);
        Assert.Equal(0, qbittorrent.ResumeCalls);
        Assert.False(qbittorrent.LastInspectFetchMetadata);
    }

    [Fact]
    public async Task CanceledWhileWaitingForFiles_TorrentIsStoppedNotStarted()
    {
        using var cancel = new CancellationTokenSource();
        var qbittorrent = new FakeQbittorrentClient { WebApiVersion = new Version(2, 11, 4), Files = [] };
        qbittorrent.OnEvent = name =>
        {
            if (name == "files")
            {
                cancel.Cancel();
            }
        };
        var gate = CreateGate(qbittorrent, new FakeBlacklistService());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            gate.AddPausedValidateAndResumeAsync(Order(Magnet), @"C:\tmp", cancel.Token));

        Assert.True(qbittorrent.PauseCalls >= 1);
        Assert.Equal(0, qbittorrent.ResumeCalls);
    }

    [Fact]
    public async Task FileListError_TorrentIsStoppedNotLeftRunning()
    {
        var qbittorrent = new FakeQbittorrentClient
        {
            WebApiVersion = new Version(2, 8, 5),
            FilesException = new HttpRequestException("WebUI hiccup")
        };
        var gate = CreateGate(qbittorrent, new FakeBlacklistService());

        await Assert.ThrowsAsync<HttpRequestException>(() => gate.AddPausedValidateAndResumeAsync(Order(Magnet), @"C:\tmp"));

        Assert.True(qbittorrent.PauseCalls >= 1);
    }

    [Fact]
    public async Task MetadataTimeoutBeforeAdd_AddsNothing()
    {
        var qbittorrent = new FakeQbittorrentClient
        {
            WebApiVersion = new Version(2, 15, 1),
            Inspection = new PreAddInspection { AddUrl = Magnet, MetadataTimedOut = true }
        };
        var gate = CreateGate(qbittorrent, new FakeBlacklistService());

        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.AddPausedValidateAndResumeAsync(Order(Magnet), @"C:\tmp"));

        Assert.Equal(0, qbittorrent.AddCount);
    }

    private static TorrentContentFile File(string name, long size) => new() { Name = name, Size = size };

    private static PreAddInspection Inspection(params TorrentContentFile[] files) => new()
    {
        AddUrl = Magnet,
        InfoHash = "cccc0000000000000000000000000000000000d4",
        Name = "Show.NN.S01E01.1080p",
        Files = files,
        MetadataPrefetched = true
    };

    private static TorrentCartOrder Order(string url) => new()
    {
        MediaId = 1,
        TargetKind = MediaKind.TvEpisode,
        EpisodeId = 10,
        Title = "Show.NN S01E01",
        SelectedCandidateUrl = url,
        SelectedCandidateName = "Show.NN.S01E01.1080p"
    };

    private static TorrentAddGateService CreateGate(FakeQbittorrentClient qbittorrent, FakeBlacklistService blacklist) =>
        new(
            qbittorrent,
            new FakeSettingsService(),
            new QbittorrentTorrentContentValidationService(qbittorrent, new FakeSettingsService(), new FakeAppLogger()),
            new TorrentCleanupService(qbittorrent, new FakeAppLogger()),
            blacklist,
            new FakeAppLogger());
}
