namespace media_management_app.Services;

public static class TorrentQuality
{
    private static readonly string[] RankedQualities = ["2160p", "1440p", "1080p", "720p", "480p"];

    public static IReadOnlyList<string> AllQualities => RankedQualities;

    public static string Detect(string fileName)
    {
        return RankedQualities.FirstOrDefault(quality =>
            fileName.Contains(quality, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
    }

    public static int GetRank(string quality)
    {
        if (string.IsNullOrWhiteSpace(quality))
        {
            return 0;
        }

        var index = Array.FindIndex(RankedQualities, rankedQuality =>
            string.Equals(rankedQuality, quality, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? 0 : RankedQualities.Length - index;
    }

    public static bool MatchesSelectedQuality(string quality, IReadOnlyList<string> selectedQualities)
    {
        if (selectedQualities.Count == 0)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(quality) &&
               selectedQualities.Any(selectedQuality =>
                   string.Equals(selectedQuality, quality, StringComparison.OrdinalIgnoreCase));
    }

    public static int CalculateCandidateScore(
        int qualityScore,
        int audioScore,
        int seeders,
        int identityScore,
        int episodeScore,
        CandidateScoringWeights? weights = null)
    {
        weights ??= CandidateScoringWeights.Default;
        var cappedSeeders = Math.Clamp(seeders, 0, weights.SeedersCap);
        return qualityScore * weights.QualityWeight +
               audioScore * weights.AudioWeight +
               cappedSeeders * weights.SeedersWeight +
               identityScore * weights.IdentityWeight +
               episodeScore * weights.EpisodeWeight;
    }
}
