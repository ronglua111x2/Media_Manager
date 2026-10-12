namespace media_management_app.Services;

public static class TorrentQualityScoring
{
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
