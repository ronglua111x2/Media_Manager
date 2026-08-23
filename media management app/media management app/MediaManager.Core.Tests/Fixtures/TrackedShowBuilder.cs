using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.Core.Tests.Fixtures;

public static class TrackedShowBuilder
{
    public static TrackedShow Show(
        string title = "Jujutsu Kaisen",
        int season = 3,
        int episode = 1,
        int? year = 2020,
        params string[] alternativeTitles)
    {
        return new TrackedShow
        {
            Id = 1,
            TmdbId = 100,
            Title = title,
            FirstAirYear = year,
            AlternativeTitlesJson = TrackedShow.SerializeAlternativeTitles(alternativeTitles)
        };
    }

    public static TrackedEpisode Episode(
        int season = 3,
        int episode = 1,
        string title = "Episode Title")
    {
        return new TrackedEpisode
        {
            Id = 10,
            ShowId = 1,
            SeasonNumber = season,
            EpisodeNumber = episode,
            Title = title
        };
    }

    public static TrackedMovie Movie(
        string title = "Your Name",
        int year = 2016,
        params string[] alternativeTitles)
    {
        return new TrackedMovie
        {
            Id = 2,
            TmdbId = 200,
            Title = title,
            ReleaseYear = year,
            AlternativeTitlesJson = TrackedMovie.SerializeAlternativeTitles(alternativeTitles)
        };
    }
}

public static class TorrentResultBuilder
{
    public static TorrentSearchResult Magnet(
        string fileName,
        int seeders = 20,
        long fileSize = 1_500_000_000,
        string engine = "Nyaa")
    {
        return new TorrentSearchResult
        {
            FileName = fileName,
            FileUrl = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567",
            Seeders = seeders,
            FileSize = fileSize,
            EngineName = engine
        };
    }
}

public sealed class FakeSearchTitleResolver : ISearchTitleResolver
{
    public IReadOnlyList<string> Titles { get; set; } = ["Jujutsu Kaisen"];

    public IReadOnlyList<string> Resolve(SearchTitleResolveRequest request) => Titles;

    public SearchTitleResolveRequest CreateRequest(
        SearchRecipe recipe,
        string primaryTitle,
        IReadOnlyList<string> libraryAlternativeTitles) =>
        new()
        {
            PrimaryTitle = primaryTitle,
            LibraryAlternativeTitles = libraryAlternativeTitles,
            IdentityEnabled = true
        };
}
