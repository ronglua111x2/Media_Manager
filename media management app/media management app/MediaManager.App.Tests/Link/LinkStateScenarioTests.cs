using System.IO;
using media_management_app.Common;
using media_management_app.Models;

namespace MediaManager.App.Tests.Link;

/// <summary>
/// End-to-end link-state scenarios from the parser audit (appendix B, LINK-01..07).
/// Each test states the desired behaviour; the PARSE id names the finding it covers.
/// </summary>
public sealed class LinkStateScenarioTests : IDisposable
{
    private readonly LinkTestHost _host = LinkTestHost.Create();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task EpisodeLink_CreatesHardlinkSymlinkAndAvailability()
    {
        _host.AddTorrent("H1", "Demo.S01E05.1080p.mkv");
        var showId = _host.AddShow(5001, "Demo Show", (1, 5, "Five"));
        _host.AssignEpisodeTorrent(showId, 1, 5, "H1");

        var result = await _host.Link.LinkEpisodeAsync(showId, 1, 5);
        _host.Symlinks.ReconcileAll();

        Assert.Equal(1, result.LinkedCount);
        var item = Assert.Single(_host.Items());
        Assert.True(File.Exists(item.LinkedPath));
        Assert.Contains(Path.Combine("Season 01", "Demo Show - S01E05"), item.LinkedPath);
        Assert.Equal(EpisodeAvailability.Available, _host.Availability(showId, 1, 5));
        Assert.Single(_host.JellyfinFiles());
    }

    // PARSE-24 (LINK-01, harness A): a special that becomes an orphan on re-link must move, not keep S00E08.
    [Fact]
    public async Task PackRelink_SpecialBecomesOrphan_MovesLinkAndClearsEpisode()
    {
        _host.FollowHardlinkRemovals();
        var showId = PackShow("HA", 5002, "Pack/Specials/Demo.OVA.Monaka.mkv");
        await _host.ApplyPackAsync(showId, 1, ("Pack/Specials/Demo.OVA.Monaka.mkv", PackFileClassification.MatchedSpecial, 0, 8));
        _host.Symlinks.ReconcileAll();

        var result = await _host.ApplyPackAsync(showId, 1, ("Pack/Specials/Demo.OVA.Monaka.mkv", PackFileClassification.UnmatchedExtra, null, null));
        _host.Symlinks.ReconcileAll();

        var item = Assert.Single(_host.Items());
        Assert.True(item.IsOrphanPackSpecial);
        Assert.Null(item.EpisodeNumber);
        Assert.True(File.Exists(item.LinkedPath));
        Assert.Contains(AppConstants.OrphanExtrasFolderName, item.LinkedPath);
        Assert.DoesNotContain(_host.LibraryFiles(), path => path.Contains("Season 00"));
        Assert.Equal(EpisodeAvailability.Missing, _host.Availability(showId, 0, 8));
        Assert.Contains(result.Messages, message => message.Contains("mapping changed", StringComparison.OrdinalIgnoreCase));
    }

    // PARSE-24 (LINK-01, harness B): orphans that become matched specials must get their own S00 links.
    [Fact]
    public async Task PackRelink_OrphansBecomeSpecials_EachGetsItsOwnEpisodeLink()
    {
        _host.FollowHardlinkRemovals();
        var showId = PackShow("HB", 5003, "Pack/Specials/Demo.OVA.Monaka.mkv", "Pack/Specials/Demo.OVA.Second.mkv");
        await _host.ApplyPackAsync(showId, 1,
            ("Pack/Specials/Demo.OVA.Monaka.mkv", PackFileClassification.UnmatchedExtra, null, null),
            ("Pack/Specials/Demo.OVA.Second.mkv", PackFileClassification.UnmatchedExtra, null, null));
        _host.Symlinks.ReconcileAll();

        await _host.ApplyPackAsync(showId, 1,
            ("Pack/Specials/Demo.OVA.Monaka.mkv", PackFileClassification.MatchedSpecial, 0, 8),
            ("Pack/Specials/Demo.OVA.Second.mkv", PackFileClassification.MatchedSpecial, 0, 9));
        _host.Symlinks.ReconcileAll();
        _host.Symlinks.ReconcileAll();

        var items = _host.Items().OrderBy(item => item.EpisodeNumber).ToList();
        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.False(item.IsOrphanPackSpecial));
        Assert.Equal([8, 9], items.Select(item => item.EpisodeNumber ?? -1));
        Assert.Equal(2, items.Select(item => item.LinkedPath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(items, item => Assert.True(File.Exists(item.LinkedPath)));
        Assert.Equal(EpisodeAvailability.Available, _host.Availability(showId, 0, 8));
        Assert.Equal(EpisodeAvailability.Available, _host.Availability(showId, 0, 9));
        var jellyfin = _host.JellyfinFiles();
        Assert.Equal(2, jellyfin.Count);
        Assert.DoesNotContain(jellyfin, path => path.Contains(AppConstants.OrphanExtrasFolderName));
    }

    // PARSE-12: re-linking a file with a corrected episode number must replace the wrong link.
    [Fact]
    public async Task PackRelink_CorrectedEpisodeNumber_ReplacesWrongLink()
    {
        var showId = PackShow("HC", 5004, "Pack/Demo - 02.mkv");
        await _host.ApplyPackAsync(showId, 1, ("Pack/Demo - 02.mkv", PackFileClassification.RegularEpisode, 1, 1));

        await _host.ApplyPackAsync(showId, 1, ("Pack/Demo - 02.mkv", PackFileClassification.RegularEpisode, 1, 2));

        var item = Assert.Single(_host.Items());
        Assert.Equal(2, item.EpisodeNumber);
        Assert.Contains("S01E02", item.LinkedPath);
        Assert.DoesNotContain(_host.LibraryFiles(), path => path.Contains("S01E01"));
        Assert.Equal(EpisodeAvailability.Missing, _host.Availability(showId, 1, 1));
        Assert.Equal(EpisodeAvailability.Available, _host.Availability(showId, 1, 2));
    }

    // PARSE-32 (LINK-09): a second file for an already linked episode must not create a second link.
    [Fact]
    public async Task SecondFileForLinkedEpisode_IsSkipped()
    {
        _host.AddTorrent("HSINGLE", "Demo.S01E05.1080p.mkv");
        var showId = PackShow("HPACK", 5005, "Pack/Demo.S01E05.mp4");
        _host.AssignEpisodeTorrent(showId, 1, 5, "HSINGLE");
        await _host.Link.LinkEpisodeAsync(showId, 1, 5);

        var result = await _host.ApplyPackAsync(showId, 1, ("Pack/Demo.S01E05.mp4", PackFileClassification.RegularEpisode, 1, 5));

        Assert.Single(_host.LibraryFiles(), path => path.Contains("S01E05"));
        Assert.Single(_host.Items(), item => item.State == ItemState.Linked && item.EpisodeNumber == 5);
        Assert.Contains(result.Messages, message => message.Contains("already", StringComparison.OrdinalIgnoreCase));
    }

    // PARSE-28 (LINK-05, harness C): reset must stop when the library file cannot be removed.
    [Fact]
    public async Task ResetEpisode_WhenLibraryFileIsLocked_KeepsRecordAndReportsError()
    {
        _host.AddTorrent("HR", "Demo.S01E05.1080p.mkv");
        var showId = _host.AddShow(5006, "Demo Show", (1, 5, "Five"));
        _host.AssignEpisodeTorrent(showId, 1, 5, "HR");
        await _host.Link.LinkEpisodeAsync(showId, 1, 5);
        var linkedPath = _host.Items().Single().LinkedPath!;

        AutoTorrentLinkResult reset;
        using (new FileStream(linkedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            reset = _host.Link.ResetEpisodeForRedownload(showId, 1, 5);
        }

        Assert.True(reset.ErrorCount > 0);
        var item = Assert.Single(_host.Items());
        Assert.Equal(linkedPath, item.LinkedPath);
        Assert.Equal("HR", _host.Db.GetTrackedEpisodes(showId).Single().TorrentHash);
    }

    // PARSE-28 (LINK-05): the movie reset has the same guard.
    [Fact]
    public async Task ResetMovie_WhenLibraryFileIsLocked_KeepsRecordAndReportsError()
    {
        _host.AddTorrent("HMV", "Demo.Movie.2016.1080p.mkv");
        var movieId = _host.Db.UpsertTrackedMovie(new TrackedMovie { TmdbId = 9002, Title = "Demo Movie", ReleaseYear = 2016 });
        _host.Db.UpdateTrackedMovieTorrent(movieId, "HMV", "HMV", "uploading", 1);
        await _host.Link.LinkMovieAsync(movieId);
        var linkedPath = _host.Items().Single().LinkedPath!;

        AutoTorrentLinkResult reset;
        using (new FileStream(linkedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            reset = _host.Link.ResetMovieForRedownload(movieId);
        }

        Assert.True(reset.ErrorCount > 0);
        Assert.Equal(linkedPath, Assert.Single(_host.Items()).LinkedPath);
        Assert.Equal("HMV", _host.Db.GetTrackedMovie(movieId)!.TorrentHash);
    }

    // PARSE-28: an unlocked reset still clears everything.
    [Fact]
    public async Task ResetEpisode_WhenFileIsFree_ClearsRecordAndHash()
    {
        _host.AddTorrent("HRF", "Demo.S01E05.1080p.mkv");
        var showId = _host.AddShow(5012, "Demo Show", (1, 5, "Five"));
        _host.AssignEpisodeTorrent(showId, 1, 5, "HRF");
        await _host.Link.LinkEpisodeAsync(showId, 1, 5);

        var reset = _host.Link.ResetEpisodeForRedownload(showId, 1, 5);

        Assert.Equal(0, reset.ErrorCount);
        Assert.Empty(_host.Items());
        Assert.Empty(_host.LibraryFiles());
        Assert.True(string.IsNullOrEmpty(_host.Db.GetTrackedEpisodes(showId).Single().TorrentHash));
    }

    // PARSE-27: deleting both the download and the library file still purges the record.
    [Fact]
    public async Task Reconcile_WhenSourceAndLinkAreDeleted_PurgesRecord()
    {
        _host.AddTorrent("HP", "Demo.S01E01.mkv");
        var showId = _host.AddShow(5010, "Demo Show", (1, 1, "One"));
        _host.AssignEpisodeTorrent(showId, 1, 1, "HP");
        await _host.Link.LinkEpisodeAsync(showId, 1, 1);
        var item = _host.Items().Single();
        File.Delete(item.FilePath);
        File.Delete(item.LinkedPath!);

        await _host.Reconciliation.ReconcileAsync(TorrentReconciliationScope.All);

        Assert.Empty(_host.Items());
        Assert.Equal(EpisodeAvailability.Missing, _host.Availability(showId, 1, 1));
    }

    // PARSE-27: qBittorrent's "missingFiles" state means the files may come back; keep the record.
    [Fact]
    public async Task Reconcile_WhenQbittorrentReportsMissingFiles_KeepsRecord()
    {
        // A single-file torrent is named after its file, as qBittorrent does.
        _host.AddNamedTorrent("HM", "Demo.S01E01.mkv", "Demo.S01E01.mkv");
        var showId = _host.AddShow(5011, "Demo Show", (1, 1, "One"));
        _host.AssignEpisodeTorrent(showId, 1, 1, "HM");
        await _host.Link.LinkEpisodeAsync(showId, 1, 1);
        var item = _host.Items().Single();
        File.Delete(item.FilePath);
        File.Delete(item.LinkedPath!);
        _host.SetTorrentState("HM", "missingFiles");

        await _host.Reconciliation.ReconcileAsync(TorrentReconciliationScope.All);

        Assert.Single(_host.Items());
    }

    // PARSE-27 (LINK-04, harness D): an unreachable download/library folder must not wipe link records.
    [Fact]
    public async Task Reconcile_WhenFoldersAreUnreachable_KeepsLinkRecords()
    {
        _host.AddTorrent("HD1", "Demo.S01E01.mkv");
        _host.AddTorrent("HD2", "Demo.S01E02.mkv");
        var showId = _host.AddShow(5007, "Demo Show", (1, 1, "One"), (1, 2, "Two"));
        _host.AssignEpisodeTorrent(showId, 1, 1, "HD1");
        _host.AssignEpisodeTorrent(showId, 1, 2, "HD2");
        await _host.Link.LinkEpisodeAsync(showId, 1, 1);
        await _host.Link.LinkEpisodeAsync(showId, 1, 2);

        Directory.Move(_host.Downloads, _host.Downloads + "_offline");
        Directory.Move(_host.Library, _host.Library + "_offline");
        await _host.Reconciliation.ReconcileAsync(TorrentReconciliationScope.All);
        Assert.Equal(2, _host.Items().Count);

        Directory.Move(_host.Downloads + "_offline", _host.Downloads);
        Directory.Move(_host.Library + "_offline", _host.Library);
        await _host.Reconciliation.ReconcileAsync(TorrentReconciliationScope.All);

        Assert.Equal(2, _host.Items().Count(item => item.State == ItemState.Linked));
        Assert.Equal(EpisodeAvailability.Available, _host.Availability(showId, 1, 1));
        var relink = await _host.Link.LinkEpisodeAsync(showId, 1, 1);
        Assert.Equal(0, relink.ErrorCount);
    }

    // Audit L3-06: a corrected mapping swaps two files' episodes; both must end up linked under the new numbers.
    [Fact]
    public async Task PackRelink_TwoFilesSwapEpisodes_BothLinkedUnderTheirNewEpisodes()
    {
        _host.FollowHardlinkRemovals();
        var showId = PackShow("HSWP", 5034, "Pack/Demo.Part.A.mkv", "Pack/Demo.Part.B.mkv");
        await _host.ApplyPackAsync(showId, 1,
            ("Pack/Demo.Part.A.mkv", PackFileClassification.RegularEpisode, 1, 1),
            ("Pack/Demo.Part.B.mkv", PackFileClassification.RegularEpisode, 1, 2));

        var result = await _host.ApplyPackAsync(showId, 1,
            ("Pack/Demo.Part.A.mkv", PackFileClassification.RegularEpisode, 1, 2),
            ("Pack/Demo.Part.B.mkv", PackFileClassification.RegularEpisode, 1, 1));

        Assert.Equal(0, result.ErrorCount);
        Assert.DoesNotContain(result.Messages, message => message.Contains("already linked from", StringComparison.Ordinal));
        var items = _host.Items().ToDictionary(item => item.FileName, item => item);
        Assert.Equal(2, items["Demo.Part.A.mkv"].EpisodeNumber);
        Assert.Equal(1, items["Demo.Part.B.mkv"].EpisodeNumber);
        Assert.All(items.Values, item => Assert.True(item.State == ItemState.Linked && File.Exists(item.LinkedPath)));
        Assert.Equal(EpisodeAvailability.Available, _host.Availability(showId, 1, 1));
        Assert.Equal(EpisodeAvailability.Available, _host.Availability(showId, 1, 2));
    }

    // Audit R2-A-02: the symlink coordinator handles created links before removed ones. The removal of F1's old
    // link (E05's library path, now F2's) must not delete the Jellyfin symlink F2 now owns.
    [Fact]
    public async Task PackRelink_SwapWithCreatedEventsFirst_KeepsBothJellyfinSymlinks()
    {
        var showId = PackShow("HSW2", 5035, "Pack/Demo.Part.A.mkv", "Pack/Demo.Part.B.mkv");
        await _host.ApplyPackAsync(showId, 1,
            ("Pack/Demo.Part.A.mkv", PackFileClassification.RegularEpisode, 1, 1),
            ("Pack/Demo.Part.B.mkv", PackFileClassification.RegularEpisode, 1, 2));
        _host.Symlinks.ReconcileAll();
        Assert.Equal(2, _host.JellyfinFiles().Count);
        var removals = new List<(SourceItem Item, string LinkedPath)>();
        _host.Hub.HardlinkRemoved += (_, args) => removals.Add((args.Item, args.LinkedPath));
        _host.Hub.HardlinksRemoved += (_, batch) => removals.AddRange(batch.Items.Select(args => (args.Item, args.LinkedPath)));

        await _host.ApplyPackAsync(showId, 1,
            ("Pack/Demo.Part.A.mkv", PackFileClassification.RegularEpisode, 1, 2),
            ("Pack/Demo.Part.B.mkv", PackFileClassification.RegularEpisode, 1, 1));
        _host.Symlinks.ReconcileAll();
        Assert.NotEmpty(removals);
        foreach (var (item, linkedPath) in removals)
        {
            _host.Symlinks.RemoveItem(item, linkedPath);
        }

        Assert.Equal(2, _host.JellyfinFiles().Count);
        Assert.All(_host.Items(), item => Assert.False(string.IsNullOrWhiteSpace(item.SymlinkPath)));
    }

    // Audit L3-02: a reset while the library drive is offline must not drop the record of a file that is still there.
    [Fact]
    public void ResetEpisode_WhenLibraryIsOffline_AbortsAndKeepsTheRecord()
    {
        var showId = TwoLinkedEpisodes(5030, "HO1", "HO2");
        Directory.Move(_host.Library, _host.Library + "_offline");

        var result = _host.Link.ResetEpisodeForRedownload(showId, 1, 2);

        Directory.Move(_host.Library + "_offline", _host.Library);
        Assert.True(result.ErrorCount > 0);
        Assert.Contains(result.Messages, message => message.Contains("not reachable", StringComparison.Ordinal));
        var item = Assert.Single(_host.Items(), row => row.EpisodeNumber == 2);
        Assert.Equal(ItemState.Linked, item.State);
        Assert.True(File.Exists(item.LinkedPath));
    }

    // Audit L3-03: startup symlink reconcile with the library drive not mounted keeps the Jellyfin symlinks.
    [Fact]
    public void SymlinkReconcile_WhenLibraryIsOffline_KeepsJellyfinSymlinks()
    {
        TwoLinkedEpisodes(5031, "HO3", "HO4");
        _host.Symlinks.ReconcileAll();
        Assert.Equal(2, _host.JellyfinFiles().Count);

        Directory.Move(_host.Library, _host.Library + "_offline");
        _host.Symlinks.ReconcileAll();
        Directory.Move(_host.Library + "_offline", _host.Library);

        Assert.Equal(2, _host.JellyfinFiles().Count);
        Assert.All(_host.Items(), item => Assert.False(string.IsNullOrWhiteSpace(item.SymlinkPath)));
    }

    // Audit L3-04: the app's own cleanup deletes the empty "Shows" folder; the drive is still reachable, so an
    // episode whose library file was deleted outside the app must still become Missing.
    [Fact]
    public async Task Reconcile_AfterCleanupRemovedTheShowsFolder_StillSeesTheDeletedFile()
    {
        var showId = TwoLinkedEpisodes(5032, "HO5", "HO6");
        File.Delete(_host.Items().Single(item => item.EpisodeNumber == 1).LinkedPath!);
        Assert.Equal(0, _host.Link.ResetEpisodeForRedownload(showId, 1, 2).ErrorCount);
        Assert.False(Directory.Exists(Path.Combine(_host.Library, AppConstants.ShowsFolderName)));

        await _host.Reconciliation.ReconcileAsync(TorrentReconciliationScope.All);

        Assert.Equal(EpisodeAvailability.Missing, _host.Availability(showId, 1, 1));
    }

    // Audit L3-05: after the library folder setting changes, an old link is refused with a reason that says what
    // to do, and once the user removed the old file the reset goes through.
    [Fact]
    public void ResetEpisode_LinkInOldLibraryFolder_ExplainsAndWorksOnceTheFileIsGone()
    {
        var showId = TwoLinkedEpisodes(5033, "HO7", "HO8");
        var oldLink = _host.Items().Single(item => item.EpisodeNumber == 2).LinkedPath!;
        _host.Settings.DriveLibraryRoots[Path.GetPathRoot(_host.Root)!] = Path.Combine(_host.Root, "lib-new");

        var refused = _host.Link.ResetEpisodeForRedownload(showId, 1, 2);
        Assert.True(refused.ErrorCount > 0);
        Assert.Contains(refused.Messages, message => message.Contains("outside the current library folder", StringComparison.Ordinal));
        Assert.True(File.Exists(oldLink));

        File.Delete(oldLink);
        var reset = _host.Link.ResetEpisodeForRedownload(showId, 1, 2);

        Assert.Equal(0, reset.ErrorCount);
        Assert.DoesNotContain(_host.Items(), item => item.EpisodeNumber == 2);
    }

    private long TwoLinkedEpisodes(int tmdbId, string firstHash, string secondHash)
    {
        _host.AddTorrent(firstHash, $"Demo.{tmdbId}.S01E01.mkv");
        _host.AddTorrent(secondHash, $"Demo.{tmdbId}.S01E02.mkv");
        var showId = _host.AddShow(tmdbId, "Demo Show", (1, 1, "One"), (1, 2, "Two"));
        _host.AssignEpisodeTorrent(showId, 1, 1, firstHash);
        _host.AssignEpisodeTorrent(showId, 1, 2, secondHash);
        Assert.Equal(1, _host.Link.LinkEpisodeAsync(showId, 1, 1).GetAwaiter().GetResult().LinkedCount);
        Assert.Equal(1, _host.Link.LinkEpisodeAsync(showId, 1, 2).GetAwaiter().GetResult().LinkedCount);
        return showId;
    }

    // PARSE-25 (LINK-02, harness F): "AAC2.0" in a manually added torrent must not adopt it as episode 2.
    [Fact]
    public async Task AdoptManualTorrent_AudioTagNumber_DoesNotMatchAnotherEpisode()
    {
        _host.Settings.AutoTorrent.AutoLinkCompletedDownloads = true;
        _host.AddTorrent("H12OLD", "Farewell.Mira.S01E12.720p.WEB.mkv");
        var showId = _host.AddShow(5008, "Farewell, Mira", (1, 2, "Two"), (1, 12, "Twelve"));
        _host.AssignEpisodeTorrent(showId, 1, 12, "H12OLD");
        const string name = "Farewell.Mira.S01E12.Farewell.Mira.1080p.WEB-DL.DUAL.AAC2.0.H.264-Grp.mkv";
        _host.AddNamedTorrent("H12NEW", name, name);

        await _host.Reconciliation.ReconcileAsync(TorrentReconciliationScope.ForMedia(MediaKind.TvEpisode, showId));

        var episode2 = _host.Db.GetTrackedEpisodes(showId).Single(item => item.EpisodeNumber == 2);
        Assert.True(string.IsNullOrEmpty(episode2.TorrentHash));
        Assert.DoesNotContain(_host.Items(), item => item.EpisodeNumber == 2);
        Assert.DoesNotContain(_host.LibraryFiles(), path => path.Contains("S01E02"));
    }

    // PARSE-25: a correctly named manual torrent is still adopted and linked.
    [Fact]
    public async Task AdoptManualTorrent_MatchingEpisode_IsAdopted()
    {
        _host.Settings.AutoTorrent.AutoLinkCompletedDownloads = true;
        var showId = _host.AddShow(5013, "Demo Show", (1, 3, "Three"));
        const string name = "Demo.Show.S01E03.1080p.WEB-DL.AAC2.0.H.264-Grp.mkv";
        _host.AddNamedTorrent("HADOPT", name, name);

        await _host.Reconciliation.ReconcileAsync(TorrentReconciliationScope.ForMedia(MediaKind.TvEpisode, showId));

        Assert.Equal("HADOPT", _host.Db.GetTrackedEpisodes(showId).Single().TorrentHash);
        Assert.Equal(3, Assert.Single(_host.Items()).EpisodeNumber);
    }

    // PARSE-25: absolute anime numbering is still adopted for season 1.
    [Fact]
    public async Task AdoptManualTorrent_AbsoluteAnimeNumber_IsAdoptedForSeasonOne()
    {
        var showId = _host.AddShow(5014, "Demo Show", (1, 2, "Two"), (1, 3, "Three"));
        const string name = "[Grp] Demo Show - 03 [1080p].mkv";
        _host.AddNamedTorrent("HANIME", name, name);

        await _host.Reconciliation.ReconcileAsync(TorrentReconciliationScope.ForMedia(MediaKind.TvEpisode, showId));

        var episodes = _host.Db.GetTrackedEpisodes(showId);
        Assert.Equal("HANIME", episodes.Single(item => item.EpisodeNumber == 3).TorrentHash);
        Assert.True(string.IsNullOrEmpty(episodes.Single(item => item.EpisodeNumber == 2).TorrentHash));
    }

    // PARSE-25: a torrent tracked by another show is not adopted when reconciling one show.
    [Fact]
    public async Task AdoptManualTorrent_TorrentOfAnotherShow_IsNotAdoptedInShowScope()
    {
        var showId = _host.AddShow(5015, "Demo Show", (1, 5, "Five"));
        var otherShowId = _host.AddShow(5016, "Demo Show Extra", (1, 5, "Five"));
        const string name = "Demo.Show.Extra.S01E05.1080p.mkv";
        _host.AddNamedTorrent("HOTHER", name, name);
        _host.AssignEpisodeTorrent(otherShowId, 1, 5, "HOTHER");

        await _host.Reconciliation.ReconcileAsync(TorrentReconciliationScope.ForMedia(MediaKind.TvEpisode, showId));

        Assert.True(string.IsNullOrEmpty(_host.Db.GetTrackedEpisodes(showId).Single().TorrentHash));
    }

    // PARSE-25: a single-file torrent named as another episode is not linked to this one.
    [Fact]
    public async Task EpisodeLink_SingleFileNamedAsAnotherEpisode_IsNotLinked()
    {
        _host.AddTorrent("HWRONG", "Demo.S01E12.1080p.mkv");
        var showId = _host.AddShow(5017, "Demo Show", (1, 2, "Two"));
        _host.AssignEpisodeTorrent(showId, 1, 2, "HWRONG");

        var result = await _host.Link.LinkEpisodeAsync(showId, 1, 2);

        Assert.Equal(0, result.LinkedCount);
        Assert.Empty(_host.Items());
        Assert.Contains(result.Messages, message => message.Contains("S01E12", StringComparison.Ordinal));
    }

    // PARSE-29 (LINK-06, harness E): a library file deleted outside the app must make the episode Missing
    // and remove its dangling Jellyfin symlink.
    [Fact]
    public async Task LibraryFileDeletedOutsideApp_EpisodeBecomesMissingAndSymlinkIsRemoved()
    {
        var showId = PackShow("HE", 5009, "Pack/Demo.S01E01.mkv");
        await _host.ApplyPackAsync(showId, 1, ("Pack/Demo.S01E01.mkv", PackFileClassification.RegularEpisode, 1, 1));
        _host.Symlinks.ReconcileAll();
        File.Delete(_host.Items().Single().LinkedPath!);

        await _host.Reconciliation.ReconcileAsync(TorrentReconciliationScope.All);
        _host.Symlinks.ReconcileAll();

        Assert.Equal(EpisodeAvailability.Missing, _host.Availability(showId, 1, 1));
        Assert.Empty(_host.JellyfinFiles());
    }

    // PARSE-30 (LINK-07, harness G): removing a movie must not leave a second record that resurrects its symlink.
    [Fact]
    public void RemoveMovie_WithLegacyRowSharingLinkedPath_DoesNotResurrectSymlink()
    {
        _host.AddTorrent("HG", "Demo.Movie.2016.2160p.mkv");
        var source = Path.Combine(_host.Downloads, "Demo.Movie.2016.2160p.mkv");
        var libraryFile = Path.Combine(_host.Library, "Movies", "Demo Movie (2016)", "Demo Movie (2016).mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(libraryFile)!);
        File.Copy(source, libraryFile);
        var movieId = _host.Db.UpsertTrackedMovie(new TrackedMovie { TmdbId = 9001, Title = "Demo Movie", ReleaseYear = 2016 });
        _host.Db.UpdateSourceItem(new SourceItem
        {
            SourceRootFolder = _host.Downloads, ParentFolder = _host.Downloads, FilePath = source, FileName = Path.GetFileName(source),
            ScanText = "x", MediaKind = MediaKind.Movie, ParserPattern = ParserPattern.MovieWithYear, MovieTitle = "Demo Movie",
            MovieYear = 2016, State = ItemState.Linked, LinkedPath = libraryFile, LastSeenUtc = DateTime.UtcNow
        });
        _host.Db.UpdateSourceItem(new SourceItem
        {
            SourceRootFolder = _host.Library, ParentFolder = Path.GetDirectoryName(libraryFile)!, FilePath = libraryFile,
            FileName = Path.GetFileName(libraryFile), ScanText = "x", MediaKind = MediaKind.Movie, ParserPattern = ParserPattern.MovieWithYear,
            MovieTitle = "Demo Movie", MovieYear = 2016, MatchedTitle = "Demo Movie", MatchedYear = 2016, Provider = "tmdb",
            ProviderId = "9001", MatchAccepted = true, IsExternalImport = true, State = ItemState.Linked, LinkedPath = libraryFile,
            LastSeenUtc = DateTime.UtcNow
        });
        _host.Symlinks.ReconcileAll();
        _host.FollowHardlinkRemovals();

        _host.Link.RemoveMovieLinks(movieId);
        _host.Symlinks.ReconcileAll();

        Assert.Empty(_host.JellyfinFiles());
    }

    // Audit L3-10: same-named extras in volume/disc folders each get their own extras file.
    [Fact]
    public async Task OrphanExtras_SameNameInVolumeFolders_BothLinked()
    {
        var showId = PackShow("HVOL", 5043, "Pack/Vol.1/NCOP.mkv", "Pack/Vol.2/NCOP.mkv");

        var result = await _host.ApplyPackAsync(showId, 1,
            ("Pack/Vol.1/NCOP.mkv", PackFileClassification.UnmatchedExtra, null, null),
            ("Pack/Vol.2/NCOP.mkv", PackFileClassification.UnmatchedExtra, null, null));

        Assert.Equal(0, result.ErrorCount);
        var linked = _host.Items().Select(item => item.LinkedPath!).ToList();
        Assert.Equal(2, linked.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(linked, path => Assert.Contains(AppConstants.OrphanExtrasFolderName, path));
        Assert.Contains(linked, path => path.EndsWith("Vol.1 - NCOP.mkv", StringComparison.Ordinal));
    }

    // Audit L3-07: an old import of show A's library file that was matched to show B. Removing show B's links
    // must not delete show A's library file.
    [Fact]
    public void RemoveShowLinks_ImportMatchedToAnotherShow_KeepsTheFirstShowsFile()
    {
        _host.AddTorrent("HXA", "Demo.S01E01.mkv");
        var showA = _host.AddShow(5040, "Demo Show", (1, 1, "One"));
        _host.AssignEpisodeTorrent(showA, 1, 1, "HXA");
        Assert.Equal(1, _host.Link.LinkEpisodeAsync(showA, 1, 1).GetAwaiter().GetResult().LinkedCount);
        var libraryFile = _host.Items().Single().LinkedPath!;
        var showB = _host.AddShow(5041, "Other Show", (1, 1, "One"));
        _host.Db.UpdateSourceItem(ImportRow(libraryFile, "5041"));

        _host.Link.RemoveShowLinks(showB);

        Assert.True(File.Exists(libraryFile));
        var torrentRow = Assert.Single(_host.Items(), item => !item.IsExternalImport);
        Assert.Equal(ItemState.Linked, torrentRow.State);
        Assert.Equal(EpisodeAvailability.Available, _host.Availability(showA, 1, 1));
    }

    // Audit R2-A-08: the same case keeps show A's Jellyfin symlink too, not only its library file.
    [Fact]
    public void RemoveShowLinks_ImportMatchedToAnotherShow_KeepsTheFirstShowsSymlink()
    {
        _host.AddTorrent("HXS", "Demo.S01E01.mkv");
        var showA = _host.AddShow(5044, "Demo Show", (1, 1, "One"));
        _host.AssignEpisodeTorrent(showA, 1, 1, "HXS");
        Assert.Equal(1, _host.Link.LinkEpisodeAsync(showA, 1, 1).GetAwaiter().GetResult().LinkedCount);
        var libraryFile = _host.Items().Single().LinkedPath!;
        _host.Symlinks.ReconcileAll();
        Assert.Single(_host.JellyfinFiles());
        var showB = _host.AddShow(5045, "Other Show", (1, 1, "One"));
        _host.Db.UpdateSourceItem(ImportRow(libraryFile, "5045"));
        _host.FollowHardlinkRemovals();

        _host.Link.RemoveShowLinks(showB);

        Assert.Single(_host.JellyfinFiles());
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(_host.Items()).SymlinkPath));
    }

    // Audit L3-07: a pack reset whose library file was also imported must not abort on the import record.
    [Fact]
    public async Task PackUnlink_LibraryFileAlsoImported_RemovesBothWithoutError()
    {
        var showId = PackShow("HXP", 5042, "Pack/Demo.S01E01.mkv");
        await _host.ApplyPackAsync(showId, 1, ("Pack/Demo.S01E01.mkv", PackFileClassification.RegularEpisode, 1, 1));
        var libraryFile = _host.Items().Single().LinkedPath!;
        _host.Db.UpdateSourceItem(ImportRow(libraryFile, "5042"));

        var result = _host.Link.RemoveSeasonPackLinks(showId, 1);

        Assert.Equal(0, result.ErrorCount);
        Assert.False(File.Exists(libraryFile));
        Assert.DoesNotContain(_host.Items(), item => item.IsExternalImport);
    }

    private static SourceItem ImportRow(string libraryFile, string providerId) => new()
    {
        SourceRootFolder = Path.GetDirectoryName(libraryFile)!, ParentFolder = Path.GetDirectoryName(libraryFile)!,
        FilePath = libraryFile, FileName = Path.GetFileName(libraryFile), ScanText = "x", MediaKind = MediaKind.TvEpisode,
        ParserPattern = ParserPattern.StandardTv, ShowTitle = "Demo Show", SeasonNumber = 1, EpisodeNumber = 1,
        MatchedTitle = "Demo Show", Provider = "tmdb", ProviderId = providerId, MatchAccepted = true,
        IsExternalImport = true, State = ItemState.Linked, LinkedPath = libraryFile, LastSeenUtc = DateTime.UtcNow
    };

    // PARSE-14: orphan extras go to Jellyfin extras folders, per season when the pack had a season
    // folder, so same-named extras of different seasons no longer collide.
    [Fact]
    public async Task OrphanExtras_GoToSeasonOrShowExtrasFolders()
    {
        var showId = PackShow("HO", 5018, "Pack/Season 1/NCOP.mkv", "Pack/Season 2/Extras/NCOP.mkv", "Pack/Extras/NCED 01.mkv");

        var result = await _host.ApplyPackAsync(showId, 1,
            ("Pack/Season 1/NCOP.mkv", PackFileClassification.UnmatchedExtra, null, null),
            ("Pack/Season 2/Extras/NCOP.mkv", PackFileClassification.UnmatchedExtra, null, null),
            ("Pack/Extras/NCED 01.mkv", PackFileClassification.UnmatchedExtra, null, null));
        _host.Symlinks.ReconcileAll();

        Assert.Equal(0, result.ErrorCount);
        var library = _host.LibraryFiles().Select(path => path.Split(Path.DirectorySeparatorChar, 3)[2]).OrderBy(path => path, StringComparer.Ordinal).ToList();
        Assert.Equal(
            [
                Path.Combine("Season 01", "extras", "NCOP.mkv"),
                Path.Combine("Season 02", "extras", "NCOP.mkv"),
                Path.Combine("extras", "NCED 01.mkv")
            ],
            library);
        Assert.Equal(3, _host.JellyfinFiles().Count(path => path.Contains($"{Path.DirectorySeparatorChar}extras{Path.DirectorySeparatorChar}")));
        Assert.DoesNotContain(_host.JellyfinFiles(), path => path.Contains(AppConstants.LegacyOrphanExtrasFolderName));
    }

    // PARSE-14: an orphan linked under the former "Season Unknown" folder moves to extras: the Jellyfin
    // symlink on the next sync, the library hardlink on the next pack link.
    [Fact]
    public async Task OrphanExtras_LegacySeasonUnknownLink_MovesToExtras()
    {
        _host.FollowHardlinkRemovals();
        var showId = PackShow("HL", 5019, "Pack/Extras/NCOP.mkv");
        await _host.ApplyPackAsync(showId, 1, ("Pack/Extras/NCOP.mkv", PackFileClassification.UnmatchedExtra, null, null));

        // Recreate the old layout: hardlink and symlink under "Season Unknown".
        var item = _host.Items().Single();
        var legacyLink = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(item.LinkedPath!)!)!, AppConstants.LegacyOrphanExtrasFolderName, "NCOP.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyLink)!);
        File.Move(item.LinkedPath!, legacyLink);
        item.LinkedPath = legacyLink;
        item.State = ItemState.Linked;
        _host.Db.UpdateSourceItem(item);
        var legacySymlink = Path.Combine(_host.Jellyfin, "Shows", "Demo Show (2015) [tmdbid-5019]", AppConstants.LegacyOrphanExtrasFolderName, "NCOP.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(legacySymlink)!);
        File.WriteAllText(legacySymlink, "symlink");
        _host.SymlinkTargets[Path.GetFullPath(legacySymlink)] = legacyLink;
        _host.Db.UpdateSourceItemSymlinkPath(_host.Items().Single().Id, legacySymlink);

        // Next sync moves the Jellyfin symlink even before the pack is linked again.
        _host.Symlinks.ReconcileAll();
        var jellyfin = Assert.Single(_host.JellyfinFiles());
        Assert.Contains(Path.Combine("extras", "NCOP.mkv"), jellyfin);

        // Next pack link moves the library hardlink.
        await _host.ApplyPackAsync(showId, 1, ("Pack/Extras/NCOP.mkv", PackFileClassification.UnmatchedExtra, null, null));
        _host.Symlinks.ReconcileAll();
        var moved = Assert.Single(_host.Items());
        Assert.True(File.Exists(moved.LinkedPath));
        Assert.Contains(Path.Combine("extras", "NCOP.mkv"), moved.LinkedPath);
        Assert.DoesNotContain(_host.LibraryFiles(), path => path.Contains(AppConstants.LegacyOrphanExtrasFolderName));
        Assert.DoesNotContain(_host.JellyfinFiles(), path => path.Contains(AppConstants.LegacyOrphanExtrasFolderName));
    }

    private long PackShow(string hash, int tmdbId, params string[] files)
    {
        _host.AddTorrent(hash, files);
        var showId = _host.AddShow(
            tmdbId,
            "Demo Show",
            (0, 8, "Monaka"),
            (0, 9, "Second OVA"),
            (1, 1, "Pilot"),
            (1, 2, "Two"),
            (1, 5, "Five"));
        _host.UsePack(showId, 1, hash);
        return showId;
    }
}
