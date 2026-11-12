using media_management_app.Models;

namespace MediaManager.App.Tests.Special.Fixtures;

/// <summary>
/// Builds a fictional show (TMDB episode list + pack file list) for the special/OVA mapping tests.
/// Names are generic on purpose: no real release, group or show names (the repository is public).
/// </summary>
internal sealed class SpecialShowBuilder
{
    private readonly List<TrackedEpisode> _episodes = [];
    private readonly List<(string RelativePath, string FileName)> _files = [];
    private readonly Dictionary<string, int?> _expected = new(StringComparer.OrdinalIgnoreCase);

    public SpecialShowBuilder(string title, int tmdbId)
    {
        Title = title;
        TmdbId = tmdbId;
    }

    public string Title { get; }

    public int TmdbId { get; }

    /// <summary>Adds <paramref name="count"/> weekly regular episodes starting on <paramref name="firstAirDate"/>.</summary>
    public SpecialShowBuilder Season(int seasonNumber, int count, DateTime firstAirDate)
    {
        for (var episode = 1; episode <= count; episode++)
        {
            _episodes.Add(Episode(seasonNumber, episode, $"Episode {episode}", firstAirDate.AddDays(7 * (episode - 1))));
        }

        return this;
    }

    public SpecialShowBuilder Special(int episodeNumber, string title, DateTime? airDate)
    {
        _episodes.Add(Episode(0, episodeNumber, title, airDate));
        return this;
    }

    /// <summary>Adds regular pack files <c>{folder}/{prefix}{SxxEyy}{suffix}</c> for every episode of the season.</summary>
    public SpecialShowBuilder RegularFiles(int seasonNumber, string folder, Func<int, string> fileName)
    {
        foreach (var episode in _episodes.Where(item => item.SeasonNumber == seasonNumber).OrderBy(item => item.EpisodeNumber))
        {
            AddFile($"{folder}/{fileName(episode.EpisodeNumber)}");
        }

        return this;
    }

    /// <summary>Adds a special/OVA file and the S00 episode it really is (null = not a TMDB special).</summary>
    public SpecialShowBuilder SpecialFile(string relativePath, int? expectedS00E)
    {
        AddFile(relativePath);
        _expected[relativePath] = expectedS00E;
        return this;
    }

    public SpecialCase Build(string name, string note) =>
        new(name, Title, TmdbId, _episodes.ToList(), _files.ToList(), new Dictionary<string, int?>(_expected, StringComparer.OrdinalIgnoreCase), note);

    private void AddFile(string relativePath) =>
        _files.Add((relativePath, relativePath[(relativePath.LastIndexOf('/') + 1)..]));

    public static TrackedEpisode Episode(int season, int episode, string title, DateTime? airDate) =>
        new()
        {
            Id = (season * 1000) + episode,
            ShowId = 1,
            SeasonNumber = season,
            EpisodeNumber = episode,
            Title = title,
            AirDate = airDate
        };
}

/// <summary>A fictional show plus a pack, with the S00 episode each special file really is.</summary>
internal sealed record SpecialCase(
    string Name,
    string ShowTitle,
    int TmdbId,
    IReadOnlyList<TrackedEpisode> Episodes,
    IReadOnlyList<(string RelativePath, string FileName)> Files,
    IReadOnlyDictionary<string, int?> ExpectedS00E,
    string Note)
{
    public IReadOnlyList<TrackedEpisode> Specials =>
        Episodes.Where(episode => episode.SeasonNumber == 0).OrderBy(episode => episode.EpisodeNumber).ToList();

    public IReadOnlyList<TrackedEpisode> RegularEpisodes =>
        Episodes.Where(episode => episode.SeasonNumber > 0).ToList();

    public IReadOnlyList<TrackedSeason> Seasons =>
        Episodes.Where(episode => episode.SeasonNumber > 0)
            .GroupBy(episode => episode.SeasonNumber)
            .Select(group => new TrackedSeason
            {
                ShowId = 1,
                SeasonNumber = group.Key,
                EpisodeCount = group.Count()
            })
            .ToList();

    public override string ToString() => Name;
}
