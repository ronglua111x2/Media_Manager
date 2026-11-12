using System.IO;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;
using MediaManager.App.Tests.Cleanup;
using Microsoft.Data.Sqlite;

namespace MediaManager.App.Tests.Pack;

// PARSE-06: a file holding two episodes is linked once as "Show - S01E01-E02.ext" and both episodes
// count as available. The DB part runs on a temporary SQLite file, never the user's state folder.
public sealed class MultiEpisodeLinkTests : IDisposable
{
    private readonly string _stateFolder = Path.Combine(Path.GetTempPath(), "mm-multi-episode-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void BuildOutputPath_MultiEpisodeItem_NamesTheRange()
    {
        var hardlinks = new HardlinkService(new FakeAppLogger(), null!, null!, null!);
        var item = EpisodeItem(@"D:\dl\Pack\Show.S01E01E02.mkv", season: 1, episode: 1, lastEpisode: 2);

        var path = hardlinks.BuildOutputPath(item, @"L:\lib");

        Assert.Equal(@"L:\lib\Show (2020) [tmdbid-123]\Season 01\Show - S01E01-E02.mkv", path);
    }

    [Fact]
    public void BuildOutputPath_SingleEpisodeItem_IsUnchanged()
    {
        var hardlinks = new HardlinkService(new FakeAppLogger(), null!, null!, null!);
        var item = EpisodeItem(@"D:\dl\Pack\Show.S01E03.mkv", season: 1, episode: 3, lastEpisode: null);

        Assert.Equal(@"L:\lib\Show (2020) [tmdbid-123]\Season 01\Show - S01E03.mkv", hardlinks.BuildOutputPath(item, @"L:\lib"));
    }

    [Fact]
    public void LastEpisodeNumber_RoundTripsAndMarksWholeRangeAvailable()
    {
        var logger = new FakeAppLogger();
        var database = new DatabaseService(logger);
        database.Initialize(_stateFolder);
        var showId = database.UpsertTrackedShow(new TrackedShow { TmdbId = 123, Title = "Show" });
        for (var episode = 1; episode <= 3; episode++)
        {
            database.UpsertTrackedEpisode(new TrackedEpisode { ShowId = showId, SeasonNumber = 1, EpisodeNumber = episode, Title = $"Episode {episode}" });
        }

        database.UpsertSourceItem(EpisodeItem(@"D:\dl\Pack\Show.S01E01E02.mkv", season: 1, episode: 1, lastEpisode: 2));

        Assert.Equal(2, Assert.Single(database.GetSourceItems()).LastEpisodeNumber);

        new TrackedShowService(database, null!, null!, null!, logger).RefreshAvailability(showId);

        var availability = database.GetTrackedEpisodes(showId).ToDictionary(episode => episode.EpisodeNumber, episode => episode.Availability);
        Assert.Equal(EpisodeAvailability.Available, availability[1]);
        Assert.Equal(EpisodeAvailability.Available, availability[2]);
        Assert.Equal(EpisodeAvailability.Missing, availability[3]);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_stateFolder))
        {
            Directory.Delete(_stateFolder, recursive: true);
        }
    }

    private static SourceItem EpisodeItem(string filePath, int season, int episode, int? lastEpisode) =>
        new()
        {
            SourceRootFolder = @"D:\dl",
            ParentFolder = Path.GetDirectoryName(filePath)!,
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            ScanText = Path.GetFileName(filePath),
            MediaKind = MediaKind.TvEpisode,
            ShowTitle = "Show",
            MatchedTitle = "Show",
            MatchedYear = 2020,
            Provider = "tmdb",
            ProviderId = "123",
            MatchAccepted = true,
            SeasonNumber = season,
            EpisodeNumber = episode,
            LastEpisodeNumber = lastEpisode,
            State = ItemState.Linked
        };
}
