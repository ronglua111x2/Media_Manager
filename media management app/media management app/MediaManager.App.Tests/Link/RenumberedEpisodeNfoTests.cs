using System.IO;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;
using media_management_app.Services.Symlink;

namespace MediaManager.App.Tests.Link;

/// <summary>
/// Through the real symlink coordinator: when TMDB renumbers an episode-group show, the NFO next to each moved
/// Jellyfin symlink describes the episode that is there now (audit L3-09).
/// </summary>
public sealed class RenumberedEpisodeNfoTests : IDisposable
{
    private readonly LinkTestHost _host = LinkTestHost.Create();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task EpisodeGroupShow_MovedLinks_GetTheNfoOfTheirEpisode()
    {
        var logger = TestFake<IAppLogger>.Create();
        var nfoWriter = new NfoWriterService(logger);
        foreach (var episode in new[] { 1, 2 })
        {
            var name = $"Demo.S01E{episode:00}.1080p";
            _host.AddNamedTorrent($"HNFO{episode}", name, name + ".mkv");
        }

        var showId = _host.AddShow(6120, "Demo Show", (1, 1, "Episode 121"), (1, 2, "Episode 122"));
        _host.Db.UpdateTrackedShowEpisodeOrganization(showId, "group-1", "Arcs");
        foreach (var episode in new[] { 1, 2 })
        {
            _host.AssignEpisodeTorrent(showId, 1, episode, $"HNFO{episode}");
            await _host.Link.LinkEpisodeAsync(showId, 1, episode);
        }

        _host.Symlinks.ReconcileAll();
        _host.TmdbDetails = Details(6120, (1, 1, 121), (1, 2, 122));
        await _host.Shows.RefreshShowAsync(_host.Db.GetTrackedShow(showId)!);
        var show = _host.Db.GetTrackedShow(showId)!;
        foreach (var item in _host.Items())
        {
            // Subtitles next to the symlink belong to its file and follow it.
            var subtitle = item.EpisodeNumber == 1 ? ".eng.srt" : ".srt";
            File.WriteAllText(Path.ChangeExtension(item.SymlinkPath!, null) + subtitle, $"subtitle {item.EpisodeNumber}");
            nfoWriter.WriteEpisodeNfoIfNeeded(
                item.SymlinkPath!,
                _host.Db.GetTrackedEpisodes(showId).Single(episode => episode.EpisodeNumber == item.EpisodeNumber),
                show);
        }

        using var coordinator = new SymlinkCoordinatorService(
            TestFake<ISettingsService>.Create((method, _) => method.Name == "get_Current" ? _host.Settings : null),
            _host.Symlinks,
            _host.Hub,
            _host.Db,
            TestFake<IPosterImageService>.Create(),
            TestFake<IWindowsNotificationService>.Create(),
            nfoWriter,
            new SymlinkSubtitleCleanupService(logger),
            TestFake<IJellyfinLibraryRefreshService>.Create(),
            logger);
        var moved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _host.Hub.SymlinkStateChanged += (_, _) => moved.TrySetResult();

        _host.TmdbDetails = Details(6120, (1, 1, 129), (1, 2, 121), (1, 3, 122));
        await _host.Shows.RefreshShowAsync(_host.Db.GetTrackedShow(showId)!);
        await moved.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var files = _host.JellyfinFiles().Select(Path.GetFileName).Order().ToList();
        Assert.Equal(
            [
                "Demo Show - S01E02.eng.srt", "Demo Show - S01E02.mkv", "Demo Show - S01E02.nfo",
                "Demo Show - S01E03.mkv", "Demo Show - S01E03.nfo", "Demo Show - S01E03.srt", "tvshow.nfo"
            ],
            files);
        Assert.Equal("subtitle 1", File.ReadAllText(JellyfinPath("Demo Show - S01E02.eng.srt")));
        Assert.Equal("subtitle 2", File.ReadAllText(JellyfinPath("Demo Show - S01E03.srt")));
        var second = File.ReadAllText(JellyfinPath("Demo Show - S01E02.nfo"));
        Assert.Contains("<title>Episode 121</title>", second);
        Assert.Contains("<episode>2</episode>", second);
        var third = File.ReadAllText(JellyfinPath("Demo Show - S01E03.nfo"));
        Assert.Contains("<title>Episode 122</title>", third);
        Assert.Contains("<episode>3</episode>", third);
    }

    private string JellyfinPath(string fileName) =>
        Path.Combine(_host.Jellyfin, _host.JellyfinFiles().Single(path => Path.GetFileName(path) == fileName));

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
