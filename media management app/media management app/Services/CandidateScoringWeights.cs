namespace media_management_app.Services;

public enum SizePreferenceMode
{
    Off = 0,
    PreferLarger = 1,
    PreferSmaller = 2
}

public sealed record CandidateScoringWeights(
    int QualityWeight,
    int AudioWeight,
    int SeedersWeight,
    int SeedersCap,
    int IdentityWeight,
    int EpisodeWeight,
    int SizeWeight,
    SizePreferenceMode SizePreference,
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
        SizeWeight: 500_000,
        SizePreference: SizePreferenceMode.PreferLarger,
        SeasonMatchScorePerSeason: 10,
        SingleSeasonBoost: 5000,
        PackExtrasPriorityEnabled: true,
        PackExtrasPriorityScore: 2500);
}
