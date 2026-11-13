using System.IO;
using media_management_app.Common;
using media_management_app.Models;

namespace MediaManager.App.Tests.Link;

/// <summary>Episode numbering changes: switching episode organization (PARSE-26) and TMDB renumbering (PARSE-31).</summary>
public sealed class EpisodeRenumberingTests : IDisposable
{
    private readonly LinkTestHost _host = LinkTestHost.Create();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task SwitchEpisodeOrganization_RemovesOldLinksAndRecords()
    {
        _host.FollowHardlinkRemovals();
        var showId = LinkedShow(5020, "HSW");
        _host.Symlinks.ReconcileAll();
        _host.TmdbDetails = Details(5020, (1, 1), (2, 1));

        var rebuilt = await _host.Shows.SwitchEpisodeOrganizationAsync(_host.Db.GetTrackedShow(showId)!, "group-1", "Arcs");

        Assert.Equal("group-1", rebuilt.EpisodeGroupId);
        Assert.Empty(_host.Items());
        Assert.Empty(_host.LibraryFiles());
        Assert.Empty(_host.JellyfinFiles());
        var episodes = _host.Db.GetTrackedEpisodes(showId);
        Assert.Equal([(1, 1), (2, 1)], episodes.Select(item => (item.SeasonNumber, item.EpisodeNumber)));
        Assert.All(episodes, item => Assert.Equal(EpisodeAvailability.Missing, item.Availability));
    }

    [Fact]
    public async Task SwitchEpisodeOrganization_WhenLinkIsLocked_ChangesNothing()
    {
        var showId = LinkedShow(5021, "HLK");
        _host.TmdbDetails = Details(5021, (1, 1), (2, 1));
        var linkedPath = _host.Items().Single().LinkedPath!;

        using (new FileStream(linkedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _host.Shows.SwitchEpisodeOrganizationAsync(_host.Db.GetTrackedShow(showId)!, "group-1", "Arcs"));
        }

        Assert.Null(_host.Db.GetTrackedShow(showId)!.EpisodeGroupId);
        Assert.Equal(linkedPath, Assert.Single(_host.Items()).LinkedPath);
        Assert.Contains(_host.Db.GetTrackedEpisodes(showId), item => item.SeasonNumber == 1 && item.EpisodeNumber == 13);
    }

    // Audit R2-A-05: one link open in a player must stop the change before any other link is removed.
    [Fact]
    public async Task SwitchEpisodeOrganization_OneOfSeveralLinksLocked_LeavesEveryLinkInPlace()
    {
        _host.AddTorrent("HLA", "Demo.S01E01.1080p.mkv");
        _host.AddTorrent("HLB", "Demo.S01E02.1080p.mkv");
        var showId = _host.AddShow(5028, "Demo Show", (1, 1, "One"), (1, 2, "Two"));
        _host.AssignEpisodeTorrent(showId, 1, 1, "HLA");
        _host.AssignEpisodeTorrent(showId, 1, 2, "HLB");
        await _host.Link.LinkEpisodeAsync(showId, 1, 1);
        await _host.Link.LinkEpisodeAsync(showId, 1, 2);
        _host.TmdbDetails = Details(5028, (1, 1), (2, 1));
        var links = _host.Items().ToDictionary(item => item.EpisodeNumber!.Value, item => item.LinkedPath!);

        using (new FileStream(links[1], FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _host.Shows.SwitchEpisodeOrganizationAsync(_host.Db.GetTrackedShow(showId)!, "group-1", "Arcs"));
        }

        Assert.True(File.Exists(links[2]));
        Assert.All(_host.Items(), item => Assert.Equal(ItemState.Linked, item.State));
        Assert.Equal(2, _host.Items().Count);
    }

    // Audit L3-01: the TMDB fetch failed after every link had been deleted.
    [Fact]
    public async Task SwitchEpisodeOrganization_WhenTmdbFetchFails_ChangesNothing()
    {
        var showId = LinkedShow(5024, "HTF");
        _host.TmdbDetails = null;
        var linkedPath = _host.Items().Single().LinkedPath!;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _host.Shows.SwitchEpisodeOrganizationAsync(_host.Db.GetTrackedShow(showId)!, "group-1", "Arcs"));

        Assert.True(File.Exists(linkedPath));
        Assert.Equal(linkedPath, Assert.Single(_host.Items()).LinkedPath);
        Assert.Equal(3, _host.Db.GetTrackedEpisodes(showId).Count);
        Assert.Null(_host.Db.GetTrackedShow(showId)!.EpisodeGroupId);
    }

    // Audit L3-01: with the download removed, the library hardlink is the only copy of the media.
    [Fact]
    public async Task SwitchEpisodeOrganization_WhenDownloadIsGone_RefusesAndKeepsTheLibraryFile()
    {
        var showId = LinkedShow(5025, "HGN");
        _host.TmdbDetails = Details(5025, (1, 1), (2, 1));
        var item = _host.Items().Single();
        File.Delete(item.FilePath);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _host.Shows.SwitchEpisodeOrganizationAsync(_host.Db.GetTrackedShow(showId)!, "group-1", "Arcs"));

        Assert.Contains("only copy", error.Message);
        Assert.True(File.Exists(item.LinkedPath));
        Assert.Single(_host.Items());
        Assert.Null(_host.Db.GetTrackedShow(showId)!.EpisodeGroupId);
    }

    // Audit L3-01: re-applying the same organization with the same numbering must not unlink anything.
    [Fact]
    public async Task SwitchEpisodeOrganization_SameRouteSameNumbering_KeepsLinks()
    {
        var showId = LinkedShow(5026, "HSR");
        _host.TmdbDetails = TitledDetails(5026, (1, 1, "One"), (1, 2, "Two"), (1, 13, "Thirteen"));
        var linkedPath = _host.Items().Single().LinkedPath!;

        await _host.Shows.SwitchEpisodeOrganizationAsync(_host.Db.GetTrackedShow(showId)!, null, null);

        Assert.True(File.Exists(linkedPath));
        Assert.Equal(linkedPath, Assert.Single(_host.Items()).LinkedPath);
        Assert.Equal(EpisodeAvailability.Available, _host.Availability(showId, 1, 13));
    }

    [Fact]
    public async Task SwitchEpisodeOrganization_SameRouteRenumbered_StillRebuilds()
    {
        _host.FollowHardlinkRemovals();
        var showId = LinkedShow(5027, "HRN");
        _host.TmdbDetails = TitledDetails(5027, (1, 1, "One"), (1, 2, "Two"), (1, 13, "A Different Episode"));

        await _host.Shows.SwitchEpisodeOrganizationAsync(_host.Db.GetTrackedShow(showId)!, null, null);

        Assert.Empty(_host.Items());
        Assert.Equal(EpisodeAvailability.Missing, _host.Availability(showId, 1, 13));
    }

    [Fact]
    public async Task RefreshShow_EpisodeRemovedUpstream_DeletesUnusedAndFlagsLinked()
    {
        var showId = LinkedShow(5022, "HRF");
        _host.TmdbDetails = Details(5022, (1, 1), (2, 1));

        await _host.Shows.RefreshShowAsync(_host.Db.GetTrackedShow(showId)!);

        var episodes = _host.Db.GetTrackedEpisodes(showId);
        Assert.DoesNotContain(episodes, item => item.SeasonNumber == 1 && item.EpisodeNumber == 2);
        var linked = Assert.Single(episodes, item => item.SeasonNumber == 1 && item.EpisodeNumber == 13);
        Assert.True(linked.IsRemovedUpstream);
        Assert.False(Assert.Single(episodes, item => item.SeasonNumber == 1 && item.EpisodeNumber == 1).IsRemovedUpstream);
        Assert.Contains(episodes, item => item.SeasonNumber == 2 && item.EpisodeNumber == 1);

        // TMDB lists the episode again: the flag is cleared.
        _host.TmdbDetails = Details(5022, (1, 1), (1, 13), (2, 1));
        await _host.Shows.RefreshShowAsync(_host.Db.GetTrackedShow(showId)!);
        Assert.False(_host.Db.GetTrackedEpisodes(showId).Single(item => item.EpisodeNumber == 13 && item.SeasonNumber == 1).IsRemovedUpstream);
    }

    [Fact]
    public async Task RefreshShow_SeasonMissingFromResponse_KeepsItsEpisodes()
    {
        var showId = LinkedShow(5023, "HPT");
        _host.TmdbDetails = Details(5023, (2, 1));

        await _host.Shows.RefreshShowAsync(_host.Db.GetTrackedShow(showId)!);

        var season1 = _host.Db.GetTrackedEpisodes(showId).Where(item => item.SeasonNumber == 1).ToList();
        Assert.Equal(3, season1.Count);
        Assert.All(season1, item => Assert.False(item.IsRemovedUpstream));
    }

    /// <summary>Show with S01E01, S01E02 and S01E13, where S01E13 is linked from a single-episode torrent.</summary>
    private long LinkedShow(int tmdbId, string hash)
    {
        _host.AddTorrent(hash, "Demo.S01E13.1080p.mkv");
        var showId = _host.AddShow(tmdbId, "Demo Show", (1, 1, "One"), (1, 2, "Two"), (1, 13, "Thirteen"));
        _host.AssignEpisodeTorrent(showId, 1, 13, hash);
        _host.Link.LinkEpisodeAsync(showId, 1, 13).GetAwaiter().GetResult();
        _host.Db.UpdateTrackedEpisodeTorrent(
            _host.Db.GetTrackedEpisodes(showId).Single(item => item.EpisodeNumber == 13).Id,
            string.Empty,
            string.Empty,
            string.Empty,
            0);
        Assert.Single(_host.Items());
        return showId;
    }

    private static TmdbShowDetails TitledDetails(int tmdbId, params (int Season, int Episode, string Title)[] episodes)
    {
        var details = Details(tmdbId, episodes.Select(item => (item.Season, item.Episode)).ToArray());
        foreach (var episode in details.Seasons.SelectMany(season => season.Episodes))
        {
            episode.Title = episodes.Single(item => item.Season == episode.SeasonNumber && item.Episode == episode.EpisodeNumber).Title;
        }

        return details;
    }

    private static TmdbShowDetails Details(int tmdbId, params (int Season, int Episode)[] episodes) => new()
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
                    SeasonNumber = item.Season,
                    EpisodeNumber = item.Episode,
                    Title = $"Episode {item.Episode}"
                }).ToList()
            })
            .ToList()
    };
}
