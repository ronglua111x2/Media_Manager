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
    [Fact(Skip = "PARSE-24")]
    public async Task PackRelink_SpecialBecomesOrphan_MovesLinkAndClearsEpisode()
    {
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
    [Fact(Skip = "PARSE-24")]
    public async Task PackRelink_OrphansBecomeSpecials_EachGetsItsOwnEpisodeLink()
    {
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
    [Fact(Skip = "PARSE-12")]
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
    [Fact(Skip = "PARSE-32")]
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
    [Fact(Skip = "PARSE-28")]
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

    // PARSE-27 (LINK-04, harness D): an unreachable download/library folder must not wipe link records.
    [Fact(Skip = "PARSE-27")]
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

    // PARSE-25 (LINK-02, harness F): "AAC2.0" in a manually added torrent must not adopt it as episode 2.
    [Fact(Skip = "PARSE-25")]
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

    // PARSE-29 (LINK-06, harness E): a library file deleted outside the app must make the episode Missing
    // and remove its dangling Jellyfin symlink.
    [Fact(Skip = "PARSE-29")]
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
    [Fact(Skip = "PARSE-30")]
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
        _host.Hub.HardlinkRemoved += (_, args) => _host.Symlinks.RemoveItem(args.Item, args.LinkedPath);

        _host.Link.RemoveMovieLinks(movieId);
        _host.Symlinks.ReconcileAll();

        Assert.Empty(_host.JellyfinFiles());
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
