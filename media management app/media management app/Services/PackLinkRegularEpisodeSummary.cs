using media_management_app.Models;

namespace media_management_app.Services;

public static class PackLinkRegularEpisodeSummary
{
    public static Dictionary<int, SeasonRegularEpisodeStats> Build(
        PackTorrentInventory inventory,
        IReadOnlyList<TrackedEpisode> episodes,
        IReadOnlySet<int> coveredSeasons)
    {
        var expectedBySeason = episodes
            .Where(episode => episode.SeasonNumber > 0)
            .GroupBy(episode => episode.SeasonNumber)
            .ToDictionary(group => group.Key, group => group.Count());

        var matchedBySeason = inventory.Files
            .Where(file =>
                file.Classification == PackFileClassification.RegularEpisode &&
                file.MatchedSeasonNumber is > 0 &&
                file.MatchedEpisodeNumber is not null)
            .GroupBy(file => file.MatchedSeasonNumber!.Value)
            .ToDictionary(
                group => group.Key,
                group => group.Select(file => file.MatchedEpisodeNumber!.Value).Distinct().Count());

        var result = new Dictionary<int, SeasonRegularEpisodeStats>();
        foreach (var season in coveredSeasons.Where(season => season > 0).OrderBy(season => season))
        {
            var expected = expectedBySeason.GetValueOrDefault(season);
            var matched = matchedBySeason.GetValueOrDefault(season);
            result[season] = new SeasonRegularEpisodeStats(matched, expected);
        }

        return result;
    }

    public static string FormatSummary(IReadOnlyDictionary<int, SeasonRegularEpisodeStats> stats) =>
        stats.Count == 0
            ? "No regular episodes mapped."
            : string.Join(
                ", ",
                stats.OrderBy(pair => pair.Key)
                    .Select(pair => $"S{pair.Key:00}: {pair.Value.Matched}/{pair.Value.Expected}"));
}
