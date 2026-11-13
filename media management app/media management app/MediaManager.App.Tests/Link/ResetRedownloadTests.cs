using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.App.Tests.Link;

/// <summary>
/// Cross-review 2 (B): "Reset for redownload" keeps the torrent in qBittorrent, so the next reconcile adopted it again
/// by name and linked the same file: the reset was undone. The reset now remembers the torrent's hash on the episode,
/// season pack or movie it was reset from, and adoption skips that hash there.
/// </summary>
public sealed class ResetRedownloadTests : IDisposable
{
    private readonly LinkTestHost _host = LinkTestHost.Create();

    public ResetRedownloadTests()
    {
        _host.Settings.AutoTorrent.AutoLinkCompletedDownloads = true;
    }

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task ResetEpisode_ReconcileDoesNotAdoptTheSameTorrentAgain()
    {
        const string name = "Demo.Show.S01E05.1080p.WEB";
        _host.AddNamedTorrent("HRESET", name, name + ".mkv");
        var showId = _host.AddShow(5040, "Demo Show", (1, 5, "Five"));
        _host.AssignEpisodeTorrent(showId, 1, 5, "HRESET");
        Assert.Equal(1, (await _host.Link.LinkEpisodeAsync(showId, 1, 5)).LinkedCount);

        Assert.Equal(0, _host.Link.ResetEpisodeForRedownload(showId, 1, 5).ErrorCount);
        await _host.Reconciliation.ReconcileAsync(TorrentReconciliationScope.ForMedia(MediaKind.TvEpisode, showId));

        var episode = _host.Db.GetTrackedEpisodes(showId).Single();
        Assert.True(string.IsNullOrEmpty(episode.TorrentHash));
        Assert.True(episode.WasResetFrom("hreset"));
        Assert.Empty(_host.Items());
        Assert.Equal(EpisodeAvailability.Missing, _host.Availability(showId, 1, 5));
    }

    // Another download of the same episode is still adopted, and a second reset keeps both hashes.
    [Fact]
    public async Task ResetEpisode_AnotherTorrentIsStillAdopted_AndEveryResetHashIsKept()
    {
        _host.AddNamedTorrent("HFIRST", "Demo.Show.S01E05.1080p.WEB", "Demo.Show.S01E05.1080p.WEB.mkv");
        var showId = _host.AddShow(5041, "Demo Show", (1, 5, "Five"));
        _host.AssignEpisodeTorrent(showId, 1, 5, "HFIRST");
        await _host.Link.LinkEpisodeAsync(showId, 1, 5);
        _host.Link.ResetEpisodeForRedownload(showId, 1, 5);
        _host.AddNamedTorrent("HSECOND", "Demo.Show.S01E05.720p.HDTV", "Demo.Show.S01E05.720p.HDTV.mkv");

        await _host.Reconciliation.ReconcileAsync(TorrentReconciliationScope.ForMedia(MediaKind.TvEpisode, showId));

        Assert.Equal("HSECOND", _host.Db.GetTrackedEpisodes(showId).Single().TorrentHash);
        Assert.EndsWith("Demo.Show.S01E05.720p.HDTV.mkv", Assert.Single(_host.Items()).FilePath);

        _host.Link.ResetEpisodeForRedownload(showId, 1, 5);
        await _host.Reconciliation.ReconcileAsync(TorrentReconciliationScope.ForMedia(MediaKind.TvEpisode, showId));

        var episode = _host.Db.GetTrackedEpisodes(showId).Single();
        Assert.True(string.IsNullOrEmpty(episode.TorrentHash));
        Assert.True(episode.WasResetFrom("HFIRST"));
        Assert.True(episode.WasResetFrom("HSECOND"));
        Assert.Empty(_host.Items());
    }

    [Fact]
    public async Task ResetMovie_ReconcileDoesNotAdoptTheSameTorrentAgain()
    {
        const string name = "Demo.Movie.2016.1080p.BluRay";
        _host.AddNamedTorrent("HMOVIE", name, name + ".mkv");
        var movieId = _host.Db.UpsertTrackedMovie(new TrackedMovie { TmdbId = 9040, Title = "Demo Movie", ReleaseYear = 2016 });
        _host.Db.UpdateTrackedMovieTorrent(movieId, "HMOVIE", name, "uploading", 1);
        Assert.Equal(1, (await _host.Link.LinkMovieAsync(movieId)).LinkedCount);

        Assert.Equal(0, _host.Link.ResetMovieForRedownload(movieId).ErrorCount);
        await _host.Reconciliation.ReconcileAsync(TorrentReconciliationScope.ForMedia(MediaKind.Movie, movieId));

        var movie = _host.Db.GetTrackedMovie(movieId)!;
        Assert.True(string.IsNullOrEmpty(movie.TorrentHash));
        Assert.True(movie.WasResetFrom("HMOVIE"));
        Assert.Empty(_host.Items());
    }

    [Fact]
    public async Task ResetSeasonPack_ReconcileDoesNotAdoptTheSamePackAgain()
    {
        const string name = "Demo Show S01 Season Pack 1080p";
        _host.AddNamedTorrent("HPACK", name, "Pack/Demo.S01E01.mkv", "Pack/Demo.S01E02.mkv");
        var showId = _host.AddShow(5042, "Demo Show", (1, 1, "One"), (1, 2, "Two"));
        _host.UsePack(showId, 1, "HPACK");
        await _host.ApplyPackAsync(
            showId,
            1,
            ("Pack/Demo.S01E01.mkv", PackFileClassification.RegularEpisode, 1, 1),
            ("Pack/Demo.S01E02.mkv", PackFileClassification.RegularEpisode, 1, 2));
        Assert.Equal(2, _host.Items().Count);

        Assert.Equal(0, (await _host.Link.ResetSeasonPackForRedownloadAsync(showId, 1)).ErrorCount);
        await _host.Reconciliation.ReconcileAsync(TorrentReconciliationScope.ForMedia(MediaKind.TvEpisode, showId));

        var season = _host.Db.GetTrackedSeasons(showId).Single(item => item.SeasonNumber == 1);
        Assert.True(string.IsNullOrEmpty(season.PackTorrentHash));
        Assert.True(season.WasResetFrom("HPACK"));
        Assert.Empty(_host.Items());
    }
}
