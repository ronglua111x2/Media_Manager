namespace media_management_app.Services;

public sealed record CandidateScoringWeights(
    int QualityWeight,
    int AudioWeight,
    int SeedersWeight,
    int SeedersCap,
    int IdentityWeight,
    int EpisodeWeight,
    int SeasonMatchScorePerSeason,
    int SingleSeasonBoost,
    bool PackExtrasPriorityEnabled,
    int PackExtrasPriorityScore)
{
    public static CandidateScoringWeights Default { get; } = new(
        QualityWeight: 10_000_000,
        AudioWeight: 1_000_000,
        SeedersWeight: 100,
        SeedersCap: 99_999,
        IdentityWeight: 10,
        EpisodeWeight: 1,
        SeasonMatchScorePerSeason: 10,
        SingleSeasonBoost: 5000,
        PackExtrasPriorityEnabled: true,
        PackExtrasPriorityScore: 2500);
}
