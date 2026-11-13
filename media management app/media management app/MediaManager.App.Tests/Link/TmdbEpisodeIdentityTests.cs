using System.IO;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.App.Tests.Link;

/// <summary>
/// TMDB renumbers episodes that are already linked (a recap inserted, a season split, two episodes swapped): each row,
/// library link, Jellyfin symlink and download follows its TMDB episode id to the new number (audit L3-09).
/// </summary>
public sealed class TmdbEpisodeIdentityTests : IDisposable
{
    private readonly LinkTestHost _host = LinkTestHost.Create();

    public TmdbEpisodeIdentityTests()
    {
        _host.FollowHardlinkRemovals();
        _host.FollowHardlinkMoves();
    }

    public void Dispose() => _host.Dispose();

    // Example A: before the fix every key still existed, so E04..E06 stayed Available on the wrong files and the old
    // E06 was hunted again as E07.
    [Fact]
    public async Task RecapInserted_LinksRowsAndDownloadsFollowTheirEpisodes()
    {
        var showId = await LinkedShowAsync(6101, Ep(1, 1, 101), Ep(1, 2, 102), Ep(1, 3, 103), Ep(1, 4, 104), Ep(1, 5, 105), Ep(1, 6, 106));
        var e04Hash = Hash(6101, 1, 4);

        _host.TmdbDetails = Details(6101, Ep(1, 1, 101), Ep(1, 2, 102), Ep(1, 3, 103), Ep(1, 4, 999), Ep(1, 5, 104), Ep(1, 6, 105), Ep(1, 7, 106));
        await RefreshAsync(showId);

        AssertLibrary(("S01E01", "S01E01"), ("S01E02", "S01E02"), ("S01E03", "S01E03"), ("S01E05", "S01E04"), ("S01E06", "S01E05"), ("S01E07", "S01E06"));
        AssertJellyfinFollowsLibrary("S01E01", "S01E02", "S01E03", "S01E05", "S01E06", "S01E07");

        var recap = Assert.Single(Episodes(showId), item => item.Availability == EpisodeAvailability.Missing);
        Assert.Equal((1, 4, 999), (recap.SeasonNumber, recap.EpisodeNumber, recap.TmdbEpisodeId));
        Assert.Null(recap.TorrentHash);
        var moved = Episode(showId, 1, 5);
        Assert.Equal(104, moved.TmdbEpisodeId);
        Assert.Equal(e04Hash, moved.TorrentHash);
        Assert.Equal("Episode 104", moved.Title);
        Assert.False(moved.IsRemovedUpstream);
        Assert.Equal([1, 2, 3, 5, 6, 7], _host.Items().Select(item => item.EpisodeNumber!.Value).Order());

        // Nothing is linked twice: a reconcile pass with auto-link on leaves the moved links and the recap alone.
        _host.Settings.AutoTorrent.AutoLinkCompletedDownloads = true;
        await _host.Reconciliation.ReconcileAsync(TorrentReconciliationScope.All);

        AssertLibrary(("S01E01", "S01E01"), ("S01E02", "S01E02"), ("S01E03", "S01E03"), ("S01E05", "S01E04"), ("S01E06", "S01E05"), ("S01E07", "S01E06"));
        Assert.Equal(6, _host.Items().Count);
        Assert.Equal(EpisodeAvailability.Missing, _host.Availability(showId, 1, 4));
        Assert.Null(Episode(showId, 1, 4).TorrentHash);
    }

    // Example B: TMDB splits one 4-episode season into two seasons of 2.
    [Fact]
    public async Task SeasonSplit_SecondHalfMovesToTheNewSeason()
    {
        var showId = await LinkedShowAsync(6102, Ep(1, 1, 201), Ep(1, 2, 202), Ep(1, 3, 203), Ep(1, 4, 204));

        _host.TmdbDetails = Details(6102, Ep(1, 1, 201), Ep(1, 2, 202), Ep(2, 1, 203), Ep(2, 2, 204));
        await RefreshAsync(showId);

        AssertLibrary(("S01E01", "S01E01"), ("S01E02", "S01E02"), ("S02E01", "S01E03"), ("S02E02", "S01E04"));
        Assert.Contains(_host.LibraryFiles(), path => path.Contains("Season 02") && path.EndsWith("S02E01.mkv"));
        AssertJellyfinFollowsLibrary("S01E01", "S01E02", "S02E01", "S02E02");
        Assert.Equal(
            [(1, 1), (1, 2), (2, 1), (2, 2)],
            Episodes(showId).Select(item => (item.SeasonNumber, item.EpisodeNumber)));
        Assert.All(Episodes(showId), item =>
        {
            Assert.Equal(EpisodeAvailability.Available, item.Availability);
            Assert.False(item.IsRemovedUpstream);
        });
    }

    [Fact]
    public async Task SwappedEpisodes_FilesSwapThroughTemporaryNames()
    {
        var showId = await LinkedShowAsync(6103, Ep(1, 1, 301), Ep(1, 2, 302), Ep(1, 3, 303));

        _host.TmdbDetails = Details(6103, Ep(1, 1, 301), Ep(1, 2, 303), Ep(1, 3, 302));
        await RefreshAsync(showId);

        AssertLibrary(("S01E01", "S01E01"), ("S01E02", "S01E03"), ("S01E03", "S01E02"));
        AssertJellyfinFollowsLibrary("S01E01", "S01E02", "S01E03");
        Assert.Equal(Hash(6103, 1, 3), Episode(showId, 1, 2).TorrentHash);
        Assert.Equal(Hash(6103, 1, 2), Episode(showId, 1, 3).TorrentHash);
    }

    // The library hardlink is the only copy once its download is deleted: it is renamed, never re-created.
    [Fact]
    public async Task DownloadDeleted_LibraryFileIsStillMoved()
    {
        var showId = await LinkedShowAsync(6104, Ep(1, 1, 401), Ep(1, 2, 402));
        var item = _host.Items().Single(candidate => candidate.EpisodeNumber == 2);
        File.Delete(item.FilePath);

        _host.TmdbDetails = Details(6104, Ep(1, 1, 401), Ep(1, 2, 499), Ep(1, 3, 402));
        await RefreshAsync(showId);

        AssertLibrary(("S01E01", "S01E01"), ("S01E03", "S01E02"));
        var moved = _host.Items().Single(candidate => candidate.FilePath == item.FilePath);
        Assert.Equal(3, moved.EpisodeNumber);
        Assert.Equal(ItemState.Linked, moved.State);
        Assert.EndsWith("S01E03.mkv", moved.LinkedPath);
        Assert.Equal(EpisodeAvailability.Missing, _host.Availability(showId, 1, 2));
    }

    // Episode deleted on TMDB and a new one created at its number: never guess, keep the link, flag it.
    [Fact]
    public async Task VanishedEpisodeId_KeepsLinkAndFlagsIt()
    {
        var showId = await LinkedShowAsync(6105, Ep(1, 1, 501), Ep(1, 2, 502), Ep(1, 3, 503));

        _host.TmdbDetails = Details(6105, Ep(1, 1, 501), Ep(1, 2, 502), Ep(1, 3, 599));
        await RefreshAsync(showId);

        AssertLibrary(("S01E01", "S01E01"), ("S01E02", "S01E02"), ("S01E03", "S01E03"));
        var kept = Episode(showId, 1, 3);
        Assert.True(kept.IsRemovedUpstream);
        Assert.Equal(503, kept.TmdbEpisodeId);
        Assert.Equal("Episode 503", kept.Title);
        Assert.Equal(EpisodeAvailability.Available, kept.Availability);
        Assert.Contains(_host.Warnings, warning => warning.Contains("S01E03"));
    }

    // Review of L3-09 (D): a search that found nothing left a cart order without a torrent. It holds no download, so the
    // row whose id TMDB dropped is deleted with its order instead of staying as a flagged "ghost" episode.
    [Fact]
    public async Task VanishedEpisodeId_HashlessOrderDoesNotKeepTheRow()
    {
        var showId = await LinkedShowAsync(6112, Ep(1, 1, 1201));
        _host.TmdbDetails = Details(6112, Ep(1, 1, 1201), Ep(1, 2, 1202));
        await RefreshAsync(showId);
        var orderId = AddOrder(showId, 1, 2, TorrentOrderStatus.NoCandidates);

        _host.TmdbDetails = Details(6112, Ep(1, 1, 1201));
        await RefreshAsync(showId);

        Assert.DoesNotContain(Episodes(showId), item => item.EpisodeNumber == 2);
        Assert.DoesNotContain(_host.Db.GetTorrentCartOrders(MediaKind.TvEpisode, showId), order => order.Id == orderId);
    }

    // Same, when the dropped row is deleted in the transaction that moves another row onto its number.
    [Fact]
    public async Task VanishedEpisodeId_HashlessOrder_DeletedWhileAnotherRowMovesIn()
    {
        var showId = await LinkedShowAsync(6113, Ep(1, 1, 1301));
        _host.TmdbDetails = Details(6113, Ep(1, 1, 1301), Ep(1, 2, 1302));
        await RefreshAsync(showId);
        var orderId = AddOrder(showId, 1, 2, TorrentOrderStatus.Failed);

        _host.TmdbDetails = Details(6113, Ep(1, 1, 1399), Ep(1, 2, 1301));
        await RefreshAsync(showId);

        AssertLibrary(("S01E02", "S01E01"));
        Assert.Equal(1301, Episode(showId, 1, 2).TmdbEpisodeId);
        Assert.DoesNotContain(Episodes(showId), item => item.TmdbEpisodeId == 1302);
        Assert.DoesNotContain(_host.Db.GetTorrentCartOrders(MediaKind.TvEpisode, showId), order => order.Id == orderId);
    }

    // An order whose torrent was added holds a download: the row stays and is flagged.
    [Fact]
    public async Task VanishedEpisodeId_OrderWithTorrentKeepsTheRow()
    {
        var showId = await LinkedShowAsync(6114, Ep(1, 1, 1401));
        _host.TmdbDetails = Details(6114, Ep(1, 1, 1401), Ep(1, 2, 1402));
        await RefreshAsync(showId);
        var orderId = AddOrder(showId, 1, 2, TorrentOrderStatus.Downloading, torrentHash: "HORDER");

        _host.TmdbDetails = Details(6114, Ep(1, 1, 1401));
        await RefreshAsync(showId);

        Assert.True(Episode(showId, 1, 2).IsRemovedUpstream);
        Assert.Contains(_host.Db.GetTorrentCartOrders(MediaKind.TvEpisode, showId), order => order.Id == orderId);
    }

    // Protection starts with the first refresh that reads ids: rows saved before have none, so nothing moves then.
    [Fact]
    public async Task FirstRefreshWithoutStoredIds_FillsIdsAndMovesNothing()
    {
        var showId = await LinkedShowAsync(6106, fillIds: false, Ep(1, 1, 601), Ep(1, 2, 602));

        _host.TmdbDetails = Details(6106, Ep(1, 1, 601), Ep(1, 2, 699), Ep(1, 3, 602));
        await RefreshAsync(showId);

        AssertLibrary(("S01E01", "S01E01"), ("S01E02", "S01E02"));
        Assert.Equal([601, 699, 602], Episodes(showId).Select(item => item.TmdbEpisodeId!.Value));
        Assert.Equal(EpisodeAvailability.Missing, _host.Availability(showId, 1, 3));
    }

    // A library file open in a player cannot be renamed: nothing moves, the rows keep their episodes and are flagged,
    // nothing is hunted, and the next refresh moves them.
    [Fact]
    public async Task LockedLibraryFile_MovesNothingAndRetriesOnTheNextRefresh()
    {
        var showId = await LinkedShowAsync(6107, Ep(1, 1, 701), Ep(1, 2, 702));
        var firstLink = _host.Items().Single(item => item.EpisodeNumber == 1).LinkedPath!;
        _host.TmdbDetails = Details(6107, Ep(1, 1, 799), Ep(1, 2, 701), Ep(1, 3, 702));

        using (new FileStream(firstLink, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await RefreshAsync(showId);
        }

        AssertLibrary(("S01E01", "S01E01"), ("S01E02", "S01E02"));
        Assert.Equal([701, 702], Episodes(showId).Select(item => item.TmdbEpisodeId!.Value));
        Assert.All(Episodes(showId), item =>
        {
            Assert.True(item.IsRemovedUpstream);
            Assert.Equal(EpisodeAvailability.Available, item.Availability);
        });

        await RefreshAsync(showId);

        AssertLibrary(("S01E02", "S01E01"), ("S01E03", "S01E02"));
        AssertJellyfinFollowsLibrary("S01E02", "S01E03");
        Assert.Equal([799, 701, 702], Episodes(showId).Select(item => item.TmdbEpisodeId!.Value));
        Assert.All(Episodes(showId), item => Assert.False(item.IsRemovedUpstream));
        Assert.Equal(EpisodeAvailability.Missing, _host.Availability(showId, 1, 1));
    }

    // Review of L3-09: auto-link re-read the range from the file name ("S01E01E02") and moved the link back.
    [Fact]
    public async Task RangeFileFromEpisodeTorrent_StaysMovedAfterReconcile()
    {
        _host.AddNamedTorrent("HRNG", "Demo.S01E01E02.1080p", "Demo.S01E01E02.1080p.mkv");
        var showId = _host.AddShow(6113, "Demo Show", (1, 1, "Episode 1001"), (1, 2, "Episode 1002"));
        _host.AssignEpisodeTorrent(showId, 1, 1, "HRNG");
        Assert.Equal(1, (await _host.Link.LinkEpisodeAsync(showId, 1, 1)).LinkedCount);
        _host.Symlinks.ReconcileAll();
        _host.TmdbDetails = Details(6113, Ep(1, 1, 1001), Ep(1, 2, 1002));
        await RefreshAsync(showId);

        _host.TmdbDetails = Details(6113, Ep(1, 1, 1099), Ep(1, 2, 1001), Ep(1, 3, 1002));
        await RefreshAsync(showId);
        _host.Settings.AutoTorrent.AutoLinkCompletedDownloads = true;
        await _host.Reconciliation.ReconcileAsync(TorrentReconciliationScope.All);

        Assert.Equal("Demo Show - S01E02-E03.mkv", Path.GetFileName(Assert.Single(_host.LibraryFiles())));
        var item = Assert.Single(_host.Items());
        Assert.Equal((2, 3), (item.EpisodeNumber!.Value, item.LastEpisodeNumber!.Value));
        var missing = Assert.Single(Episodes(showId), episode => episode.Availability == EpisodeAvailability.Missing);
        Assert.Equal(1, missing.EpisodeNumber);
        AssertJellyfinFollowsLibrary("S01E02-E03");
    }

    // Review of L3-09: one torrent with both episode files, assigned to both rows; auto-link picked the file by the
    // row's new number and moved another link back.
    [Fact]
    public async Task BatchTorrentAssignedToSeveralRows_StaysMovedAfterReconcile()
    {
        _host.AddNamedTorrent("HBATCH", "Demo.S01.1080p", "Demo.S01E01.1080p.mkv", "Demo.S01E02.1080p.mkv");
        var showId = _host.AddShow(6114, "Demo Show", (1, 1, "Episode 1011"), (1, 2, "Episode 1012"));
        foreach (var episode in new[] { 1, 2 })
        {
            _host.AssignEpisodeTorrent(showId, 1, episode, "HBATCH");
            Assert.Equal(1, (await _host.Link.LinkEpisodeAsync(showId, 1, episode)).LinkedCount);
        }

        _host.Symlinks.ReconcileAll();
        _host.TmdbDetails = Details(6114, Ep(1, 1, 1011), Ep(1, 2, 1012));
        await RefreshAsync(showId);

        _host.TmdbDetails = Details(6114, Ep(1, 1, 1019), Ep(1, 2, 1011), Ep(1, 3, 1012));
        await RefreshAsync(showId);
        _host.Settings.AutoTorrent.AutoLinkCompletedDownloads = true;
        await _host.Reconciliation.ReconcileAsync(TorrentReconciliationScope.All);

        AssertLibrary(("S01E02", "S01E01"), ("S01E03", "S01E02"));
        AssertJellyfinFollowsLibrary("S01E02", "S01E03");
        Assert.Equal(EpisodeAvailability.Missing, _host.Availability(showId, 1, 1));
    }

    // Review of L3-09: a download still running when TMDB renumbers is named with the old number; once complete it
    // must link at the episode's new number, not be refused forever.
    [Fact]
    public async Task DownloadCompletingAfterTheMove_LinksAtTheNewNumber()
    {
        var showId = await LinkedShowAsync(6115, Ep(1, 1, 1021));
        _host.Db.UpsertTrackedEpisode(new TrackedEpisode { ShowId = showId, SeasonNumber = 1, EpisodeNumber = 2, Title = "Episode 1022" });
        _host.AssignEpisodeTorrent(showId, 1, 2, "HLATE");
        _host.TmdbDetails = Details(6115, Ep(1, 1, 1021), Ep(1, 2, 1022));
        await RefreshAsync(showId);

        _host.TmdbDetails = Details(6115, Ep(1, 1, 1029), Ep(1, 2, 1021), Ep(1, 3, 1022));
        await RefreshAsync(showId);
        Assert.Equal("HLATE", Episode(showId, 1, 3).TorrentHash);

        _host.AddNamedTorrent("HLATE", "Demo.S01E02.1080p", "Demo.S01E02.1080p.mkv");
        _host.Settings.AutoTorrent.AutoLinkCompletedDownloads = true;
        await _host.Reconciliation.ReconcileAsync(TorrentReconciliationScope.All);

        AssertLibrary(("S01E02", "S01E01"), ("S01E03", "S01E02"));
        Assert.Equal(EpisodeAvailability.Available, _host.Availability(showId, 1, 3));
        Assert.Equal(EpisodeAvailability.Missing, _host.Availability(showId, 1, 1));
    }

    // Review of L3-09: a second refresh that planned from the same rows as the first (Auto-Track and the Refresh
    // button at once) swapped the files back while the rows stayed: every link then named the other episode.
    [Fact]
    public async Task StaleRenumbering_IsRefusedAndChangesNothing()
    {
        var showId = await LinkedShowAsync(6116, Ep(1, 1, 1031), Ep(1, 2, 1032));
        var staleRows = Episodes(showId).ToDictionary(item => item.EpisodeNumber, item => item.Id);
        _host.TmdbDetails = Details(6116, Ep(1, 1, 1032), Ep(1, 2, 1031));
        await RefreshAsync(showId);

        Assert.ThrowsAny<InvalidOperationException>(() => _host.Link.RenumberEpisodes(
            showId,
            [new EpisodeRenumber(staleRows[1], (1, 1), (1, 2)), new EpisodeRenumber(staleRows[2], (1, 2), (1, 1))],
            []));

        AssertLibrary(("S01E01", "S01E02"), ("S01E02", "S01E01"));
        Assert.Equal([1032, 1031], Episodes(showId).Select(item => item.TmdbEpisodeId!.Value));
        Assert.Equal(Hash(6116, 1, 2), Episode(showId, 1, 1).TorrentHash);
    }

    // Review of L3-09: TMDB merges S02 into S01, so S02 is no longer returned; its episodes are found in S01.
    [Fact]
    public async Task SeasonMerged_EpisodesFollowIntoTheRemainingSeason()
    {
        var showId = await LinkedShowAsync(6117, Ep(1, 1, 1041), Ep(1, 2, 1042), Ep(2, 1, 1043), Ep(2, 2, 1044));

        _host.TmdbDetails = Details(6117, Ep(1, 1, 1041), Ep(1, 2, 1042), Ep(1, 3, 1043), Ep(1, 4, 1044));
        await RefreshAsync(showId);

        AssertLibrary(("S01E01", "S01E01"), ("S01E02", "S01E02"), ("S01E03", "S02E01"), ("S01E04", "S02E02"));
        Assert.Equal(
            [(1, 1, 1041), (1, 2, 1042), (1, 3, 1043), (1, 4, 1044)],
            Episodes(showId).Select(item => (item.SeasonNumber, item.EpisodeNumber, item.TmdbEpisodeId!.Value)));
        Assert.All(Episodes(showId), item => Assert.Equal(EpisodeAvailability.Available, item.Availability));
    }

    // Review of L3-09: bad TMDB data with two episodes on one number made every refresh of the show fail.
    [Fact]
    public async Task TwoEpisodesMovedOntoOneNumber_RefreshStillWorks()
    {
        var showId = _host.AddShow(6118, "Demo Show", (1, 1, "Episode 1051"), (1, 2, "Episode 1052"));
        _host.TmdbDetails = Details(6118, Ep(1, 1, 1051), Ep(1, 2, 1052));
        await RefreshAsync(showId);

        _host.TmdbDetails = Details(6118, Ep(1, 1, 1058), Ep(1, 2, 1059), Ep(1, 3, 1051), Ep(1, 3, 1052));
        await RefreshAsync(showId);

        Assert.Contains(Episodes(showId), item => item.EpisodeNumber == 3);
    }

    // Review of L3-09: "Auto-Track from E02" means from that episode; when TMDB moves it, the start follows it.
    [Fact]
    public async Task AutoTrackStart_FollowsItsEpisode()
    {
        var showId = await LinkedShowAsync(6119, Ep(1, 1, 1061), Ep(1, 2, 1062), Ep(1, 3, 1063));
        _host.Db.UpdateTrackedShowAutoTrack(showId, 1, 2);

        _host.TmdbDetails = Details(6119, Ep(1, 1, 1069), Ep(1, 2, 1061), Ep(1, 3, 1062), Ep(1, 4, 1063));
        await RefreshAsync(showId);

        var show = _host.Db.GetTrackedShow(showId)!;
        Assert.Equal((1, 3), (show.AutoTrackFromSeason, show.AutoTrackFromEpisode));
    }

    // While E01's file cannot move, the not-yet-downloaded row at E02 (the number TMDB now gives E01's episode) must not
    // stay Missing: Auto-Track would download E01's episode a second time.
    [Fact]
    public async Task LockedLibraryFile_UnusedRowOnItsNewNumber_IsNotHunted()
    {
        const string name = "Demo.S01E01.1080p";
        _host.AddNamedTorrent("HLOCK1", name, name + ".mkv");
        var showId = _host.AddShow(6112, "Demo Show", (1, 1, "Episode 711"), (1, 2, "Episode 712"));
        _host.AssignEpisodeTorrent(showId, 1, 1, "HLOCK1");
        await _host.Link.LinkEpisodeAsync(showId, 1, 1);
        _host.TmdbDetails = Details(6112, Ep(1, 1, 711), Ep(1, 2, 712));
        await RefreshAsync(showId);
        var link = Assert.Single(_host.Items()).LinkedPath!;
        _host.TmdbDetails = Details(6112, Ep(1, 1, 719), Ep(1, 2, 711), Ep(1, 3, 712));

        using (new FileStream(link, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await RefreshAsync(showId);
        }

        _host.Shows.RefreshAvailability(showId);
        Assert.Equal([(1, 1, 711), (1, 3, 712)], Episodes(showId).Select(item => (item.SeasonNumber, item.EpisodeNumber, item.TmdbEpisodeId!.Value)));
        var missing = Assert.Single(Episodes(showId), item => item.Availability == EpisodeAvailability.Missing);
        Assert.Equal(3, missing.EpisodeNumber);

        await RefreshAsync(showId);

        AssertLibrary(("S01E02", "S01E01"));
        Assert.Equal([(1, 1, 719), (1, 2, 711), (1, 3, 712)], Episodes(showId).Select(item => (item.SeasonNumber, item.EpisodeNumber, item.TmdbEpisodeId!.Value)));
        Assert.Equal(EpisodeAvailability.Available, _host.Availability(showId, 1, 2));
    }

    [Fact]
    public async Task MultiEpisodeFile_RangeMovesWhenAllItsEpisodesMoveTogether()
    {
        const string single = "Pack/Demo.S01E01.mkv";
        const string range = "Pack/Demo.S01E02E03.mkv";
        _host.AddTorrent("HRANGE", single, range);
        var showId = _host.AddShow(6108, "Demo Show", (1, 1, "One"), (1, 2, "Two"), (1, 3, "Three"));
        _host.UsePack(showId, 1, "HRANGE");
        await _host.ApplyPackEntriesAsync(
            showId,
            1,
            LinkTestHost.RegularEntry(single, 1, 1),
            LinkTestHost.RegularEntry(range, 1, 2, lastEpisode: 3));
        _host.Symlinks.ReconcileAll();
        _host.TmdbDetails = Details(6108, Ep(1, 1, 801), Ep(1, 2, 802), Ep(1, 3, 803));
        await RefreshAsync(showId);

        _host.TmdbDetails = Details(6108, Ep(1, 1, 899), Ep(1, 2, 801), Ep(1, 3, 802), Ep(1, 4, 803));
        await RefreshAsync(showId);

        Assert.Equal(
            ["Demo Show - S01E02.mkv", "Demo Show - S01E03-E04.mkv"],
            _host.LibraryFiles().Select(Path.GetFileName).Order());
        Assert.Equal("video " + range, File.ReadAllText(LibraryPath("S01E03-E04")));
        var rangeItem = _host.Items().Single(item => item.FilePath.EndsWith("Demo.S01E02E03.mkv"));
        Assert.Equal((3, 4), (rangeItem.EpisodeNumber!.Value, rangeItem.LastEpisodeNumber!.Value));
        var missing = Assert.Single(Episodes(showId), item => item.Availability == EpisodeAvailability.Missing);
        Assert.Equal((1, 1), (missing.SeasonNumber, missing.EpisodeNumber));
        AssertJellyfinFollowsLibrary("S01E02", "S01E03-E04");
    }

    [Fact]
    public async Task MultiEpisodeFile_RangeSplitApart_StaysAndIsFlagged()
    {
        const string range = "Pack/Demo.S01E01E02.mkv";
        _host.AddTorrent("HSPLIT", range);
        var showId = _host.AddShow(6109, "Demo Show", (1, 1, "One"), (1, 2, "Two"));
        _host.UsePack(showId, 1, "HSPLIT");
        await _host.ApplyPackEntriesAsync(showId, 1, LinkTestHost.RegularEntry(range, 1, 1, lastEpisode: 2));
        _host.TmdbDetails = Details(6109, Ep(1, 1, 811), Ep(1, 2, 812));
        await RefreshAsync(showId);

        // E01 stays, E02 moves away: one file can no longer name both episodes.
        _host.TmdbDetails = Details(6109, Ep(1, 1, 811), Ep(1, 2, 819), Ep(1, 3, 812));
        await RefreshAsync(showId);

        Assert.Equal("Demo Show - S01E01-E02.mkv", Path.GetFileName(Assert.Single(_host.LibraryFiles())));
        Assert.True(Episode(showId, 1, 2).IsRemovedUpstream);
        Assert.Equal(812, Episode(showId, 1, 2).TmdbEpisodeId);
        Assert.DoesNotContain(Episodes(showId), item => item.EpisodeNumber == 3);
    }

    [Fact]
    public async Task Specials_FollowTheirIds()
    {
        var showId = await LinkedShowAsync(6110, Ep(0, 1, 901), Ep(0, 2, 902));

        _host.TmdbDetails = Details(6110, Ep(0, 1, 999), Ep(0, 2, 901), Ep(0, 3, 902));
        await RefreshAsync(showId);

        Assert.Equal("video Demo.S00E01.1080p.mkv", File.ReadAllText(LibraryPath("S00E02")));
        Assert.Equal("video Demo.S00E02.1080p.mkv", File.ReadAllText(LibraryPath("S00E03")));
        Assert.Equal(2, _host.LibraryFiles().Count);
        Assert.Equal(EpisodeAvailability.Missing, _host.Availability(showId, 0, 1));
        Assert.Equal(901, Episode(showId, 0, 2).TmdbEpisodeId);
    }

    // "Import Existing" records are the library file itself (FilePath = LinkedPath): a swap renames both files and
    // both records must point at their new file.
    [Fact]
    public async Task ImportedLibraryFiles_Swapped_FollowTheirIds()
    {
        var showId = _host.AddShow(6111, "Demo Show", (1, 1, "Episode 151"), (1, 2, "Episode 152"));
        _host.Db.UpdateSourceItem(ImportedLibraryItem(6111, 1, 1));
        _host.Db.UpdateSourceItem(ImportedLibraryItem(6111, 1, 2));
        _host.Symlinks.ReconcileAll();
        _host.TmdbDetails = Details(6111, Ep(1, 1, 151), Ep(1, 2, 152));
        await RefreshAsync(showId);

        _host.TmdbDetails = Details(6111, Ep(1, 1, 152), Ep(1, 2, 151));
        await RefreshAsync(showId);

        AssertLibrary(("S01E01", "S01E02"), ("S01E02", "S01E01"));
        AssertJellyfinFollowsLibrary("S01E01", "S01E02");
        Assert.All(_host.Items(), item =>
        {
            Assert.Equal(item.FilePath, item.LinkedPath);
            Assert.Equal(item.EpisodeNumber == 1 ? "video Demo.S01E02.1080p.mkv" : "video Demo.S01E01.1080p.mkv", File.ReadAllText(item.FilePath));
        });
        Assert.All(Episodes(showId), item => Assert.Equal(EpisodeAvailability.Available, item.Availability));
    }

    /// <summary>A library file at the app's name for that episode, recorded as imported (its own record's file).</summary>
    private SourceItem ImportedLibraryItem(int tmdbId, int season, int episode)
    {
        var item = new SourceItem
        {
            FilePath = Path.Combine(_host.Downloads, "placeholder.mkv"),
            ScanText = "x",
            MediaKind = MediaKind.TvEpisode,
            ParserPattern = ParserPattern.StandardTv,
            ShowTitle = "Demo Show",
            SeasonNumber = season,
            EpisodeNumber = episode,
            MatchedTitle = "Demo Show",
            MatchedYear = 2015,
            Provider = "tmdb",
            ProviderId = tmdbId.ToString(),
            MatchAccepted = true,
            IsExternalImport = true,
            State = ItemState.Linked,
            LastSeenUtc = DateTime.UtcNow
        };
        Assert.True(_host.Hardlinks.TryBuildLibraryPath(item, out var libraryPath, out _));
        Directory.CreateDirectory(Path.GetDirectoryName(libraryPath)!);
        File.WriteAllText(libraryPath, $"video Demo.S{season:00}E{episode:00}.1080p.mkv");
        item.FilePath = libraryPath;
        item.LinkedPath = libraryPath;
        item.FileName = Path.GetFileName(libraryPath);
        item.ParentFolder = Path.GetDirectoryName(libraryPath)!;
        item.SourceRootFolder = item.ParentFolder;
        return item;
    }

    /// <summary>
    /// Show whose episodes are each linked from a single-episode torrent named after the episode
    /// ("Demo.S01E04.1080p.mkv"); the first refresh stores the TMDB ids at the same numbers.
    /// </summary>
    private Task<long> LinkedShowAsync(int tmdbId, params (int Season, int Episode, int Id)[] episodes) =>
        LinkedShowAsync(tmdbId, fillIds: true, episodes);

    private async Task<long> LinkedShowAsync(int tmdbId, bool fillIds, params (int Season, int Episode, int Id)[] episodes)
    {
        foreach (var episode in episodes)
        {
            var name = $"Demo.S{episode.Season:00}E{episode.Episode:00}.1080p";
            _host.AddNamedTorrent(Hash(tmdbId, episode.Season, episode.Episode), name, name + ".mkv");
        }

        var showId = _host.AddShow(tmdbId, "Demo Show", episodes.Select(item => (item.Season, item.Episode, $"Episode {item.Id}")).ToArray());
        foreach (var episode in episodes)
        {
            _host.AssignEpisodeTorrent(showId, episode.Season, episode.Episode, Hash(tmdbId, episode.Season, episode.Episode));
            var result = await _host.Link.LinkEpisodeAsync(showId, episode.Season, episode.Episode);
            Assert.Equal(1, result.LinkedCount);
        }

        _host.Symlinks.ReconcileAll();
        if (fillIds)
        {
            _host.TmdbDetails = Details(tmdbId, episodes);
            await RefreshAsync(showId);
            Assert.All(Episodes(showId), item => Assert.NotNull(item.TmdbEpisodeId));
        }

        return showId;
    }

    private Task RefreshAsync(long showId) => _host.Shows.RefreshShowAsync(_host.Db.GetTrackedShow(showId)!);

    private long AddOrder(long showId, int season, int episode, TorrentOrderStatus status, string torrentHash = "") =>
        _host.Db.UpsertTorrentCartOrder(new TorrentCartOrder
        {
            TargetKind = MediaKind.TvEpisode,
            MediaId = showId,
            EpisodeId = Episode(showId, season, episode).Id,
            SeasonNumber = season,
            EpisodeNumber = episode,
            Title = $"Demo Show S{season:00}E{episode:00}",
            Status = status,
            TorrentHash = torrentHash
        });

    private IReadOnlyList<TrackedEpisode> Episodes(long showId) => _host.Db.GetTrackedEpisodes(showId);

    private TrackedEpisode Episode(long showId, int season, int episode) =>
        Episodes(showId).Single(item => item.SeasonNumber == season && item.EpisodeNumber == episode);

    /// <summary>Library files as (number in the library name, number in the original download name).</summary>
    private void AssertLibrary(params (string Library, string Download)[] expected)
    {
        Assert.Equal(
            expected.Select(item => $"Demo Show - {item.Library}.mkv").Order(),
            _host.LibraryFiles().Select(Path.GetFileName).Order());
        foreach (var (library, download) in expected)
        {
            Assert.Equal($"video Demo.{download}.1080p.mkv", File.ReadAllText(LibraryPath(library)));
        }
    }

    private void AssertJellyfinFollowsLibrary(params string[] numbers)
    {
        Assert.Equal(
            numbers.Select(number => $"Demo Show - {number}.mkv").Order(),
            _host.JellyfinFiles().Select(Path.GetFileName).Order());
        foreach (var number in numbers)
        {
            var symlink = Path.Combine(_host.Jellyfin, _host.JellyfinFiles().Single(path => path.EndsWith($" - {number}.mkv")));
            Assert.Equal(LibraryPath(number), _host.SymlinkTargets[Path.GetFullPath(symlink)], ignoreCase: true);
        }
    }

    private string LibraryPath(string number) =>
        Path.GetFullPath(Path.Combine(_host.Library, _host.LibraryFiles().Single(path => path.Contains($" - {number}"))));

    private static string Hash(int tmdbId, int season, int episode) => $"H{tmdbId}S{season}E{episode}";

    private static (int Season, int Episode, int Id) Ep(int season, int episode, int id) => (season, episode, id);

    private static TmdbShowDetails Details(int tmdbId, params (int Season, int Episode, int Id)[] episodes) => new()
    {
        TmdbId = tmdbId,
        Title = "Demo Show",
        FirstAirYear = 2015,
        Seasons = episodes
            .GroupBy(item => item.Season)
            .Select(group => new TmdbSeasonDetails
            {
                SeasonNumber = group.Key,
                EpisodeCount = group.Count(),
                Episodes = group.Select(item => new TmdbEpisodeDetails
                {
                    TmdbEpisodeId = item.Id,
                    SeasonNumber = item.Season,
                    EpisodeNumber = item.Episode,
                    Title = $"Episode {item.Id}"
                }).ToList()
            })
            .ToList()
    };
}
