namespace media_management_app.Services;

public static class TorrentQuality
{
    private static readonly string[] RankedQualities = ["2160p", "1440p", "1080p", "720p", "480p"];

    private static readonly IReadOnlyDictionary<string, string[]> QualityAliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["2160p"] = ["2160p", "2160", "4k", "uhd", "ultra hd", "ultrahd"],
            ["1440p"] = ["1440p", "1440", "qhd"],
            ["1080p"] = ["1080p", "1080", "full hd", "fullhd", "fhd"],
            ["720p"] = ["720p", "720"],
            ["480p"] = ["480p", "480"]
        };

    public static IReadOnlyList<string> AllQualities => RankedQualities;

    public static string Detect(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        foreach (var quality in RankedQualities)
        {
            if (!QualityAliases.TryGetValue(quality, out var aliases))
            {
                continue;
            }

            if (aliases.Any(alias => fileName.Contains(alias, StringComparison.OrdinalIgnoreCase)))
            {
                return quality;
            }
        }

        return string.Empty;
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

    public static int CalculateSizeScore(
        long fileSizeBytes,
        long? minimumSizeBytes = null,
        long? maximumSizeBytes = null,
        CandidateScoringWeights? weights = null)
    {
        weights ??= CandidateScoringWeights.Default;
        if (fileSizeBytes <= 0 || weights.SizePreference == SizePreferenceMode.Off)
        {
            return 0;
        }

        var minimumBytes = minimumSizeBytes.GetValueOrDefault();
        var maximumBytes = maximumSizeBytes.GetValueOrDefault();
        var softLowGb = minimumBytes > 0
            ? minimumBytes / (1024d * 1024d * 1024d)
            : 0d;
        var softHighGb = maximumBytes > 0
            ? maximumBytes / (1024d * 1024d * 1024d)
            : Math.Max(softLowGb + 1d, 50d);
        if (softHighGb <= softLowGb)
        {
            softHighGb = softLowGb + 1d;
        }

        var fileSizeGb = fileSizeBytes / (1024d * 1024d * 1024d);
        var normalized = Math.Clamp((fileSizeGb - softLowGb) / (softHighGb - softLowGb), 0d, 1d);
        var linearScore = (int)Math.Round(100d * normalized);
        return weights.SizePreference == SizePreferenceMode.PreferSmaller
            ? 100 - linearScore
            : linearScore;
    }

    public static int CalculateCandidateScore(
        int qualityScore,
        int audioScore,
        int seeders,
        int identityScore,
        int episodeScore,
        CandidateScoringWeights? weights = null,
        int preferTermsScore = 0,
        int sizeScore = 0,
        int engineRankScore = 0)
    {
        weights ??= CandidateScoringWeights.Default;
        var cappedSeeders = Math.Clamp(seeders, 0, weights.SeedersCap);
        return qualityScore * weights.QualityWeight +
               audioScore * weights.AudioWeight +
               preferTermsScore * weights.AudioWeight +
               sizeScore * weights.SizeWeight +
               cappedSeeders * weights.SeedersWeight +
               identityScore * weights.IdentityWeight +
               episodeScore * weights.EpisodeWeight +
               engineRankScore * weights.EngineWeight;
    }
}
