namespace media_management_app.Models;

public sealed class PackTorrentInventory
{
    public List<int> CoveredSeasons { get; set; } = [];

    public Dictionary<int, int> EpisodeCountsBySeason { get; set; } = new();

    public List<PackFileEntry> Files { get; set; } = [];

    public List<string> Warnings { get; set; } = [];

    public int RegularEpisodeCount => Files.Count(file => file.Classification == PackFileClassification.RegularEpisode);

    public int TotalEpisodeCount => EpisodeCountsBySeason.Values.Sum();

    public int MatchedSpecialCount => Files.Count(file => file.Classification == PackFileClassification.MatchedSpecial);

    public int UnmatchedExtraCount => Files.Count(file => file.Classification == PackFileClassification.UnmatchedExtra);

    public int MovieCount => Files.Count(file => file.Classification == PackFileClassification.Movie);

    public int SkippedCount => Files.Count(file => file.Classification == PackFileClassification.Skipped);

    public string BuildSummaryText()
    {
        var parts = new List<string>();
        if (RegularEpisodeCount > 0)
        {
            parts.Add($"{RegularEpisodeCount} episodes");
        }

        if (MatchedSpecialCount > 0)
        {
            parts.Add($"{MatchedSpecialCount} specials/OVAs");
        }

        if (UnmatchedExtraCount > 0)
        {
            parts.Add($"{UnmatchedExtraCount} unmatched extras");
        }

        if (MovieCount > 0)
        {
            parts.Add($"{MovieCount} movies skipped");
        }

        return parts.Count == 0 ? "No video files found in pack." : $"Inspected: {string.Join(", ", parts)}.";
    }

    public PackContentProfile ToContentProfile()
    {
        var specialFiles = Files
            .Where(file => file.Classification is PackFileClassification.MatchedSpecial or PackFileClassification.UnmatchedExtra)
            .Select(file => file.FileName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var movieFiles = Files
            .Where(file => file.Classification == PackFileClassification.Movie)
            .Select(file => file.FileName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new PackContentProfile
        {
            CoveredSeasons = CoveredSeasons.ToList(),
            IncludesSpecials = MatchedSpecialCount > 0 ||
                               Files.Any(file => file.Classification == PackFileClassification.UnmatchedExtra &&
                                                 file.MatchReason.Contains("Special/OVA", StringComparison.OrdinalIgnoreCase)),
            IncludesOva = Files.Any(file =>
                file.FileName.Contains("ova", StringComparison.OrdinalIgnoreCase) &&
                file.Classification is PackFileClassification.MatchedSpecial or PackFileClassification.UnmatchedExtra),
            IncludesMovies = MovieCount > 0,
            MovieFileNames = movieFiles,
            SpecialFileNames = specialFiles
        };
    }

    public PackInspectionDisplay ToInspectionDisplay()
    {
        var totalEpisodes = TotalEpisodeCount > 0 ? TotalEpisodeCount : RegularEpisodeCount;
        var coversLine = CoveredSeasons.Count > 0
            ? $"Covers {string.Join(", ", CoveredSeasons.Select(season => $"S{season:00}"))}" +
              (totalEpisodes > 0 ? $" · {totalEpisodes} episodes" : string.Empty)
            : "No seasons detected";

        var unmatchedSpecials = Files.Count(file =>
            file.Classification == PackFileClassification.UnmatchedExtra &&
            file.MatchReason.Contains("Special/OVA", StringComparison.OrdinalIgnoreCase));
        var specialCount = MatchedSpecialCount + unmatchedSpecials;
        var pureExtrasCount = UnmatchedExtraCount - unmatchedSpecials;
        var extrasLine = specialCount > 0 || pureExtrasCount > 0
            ? $"{specialCount} specials/OVAs · {pureExtrasCount} extras"
            : string.Empty;

        var moviesLine = MovieCount > 0
            ? $"{MovieCount} movies"
            : string.Empty;

        return new PackInspectionDisplay
        {
            CoversLine = coversLine,
            ExtrasLine = extrasLine,
            MoviesLine = moviesLine
        };
    }

    public string BuildInspectionDetailText() => ToInspectionDisplay().ToStatusDetail();
}
