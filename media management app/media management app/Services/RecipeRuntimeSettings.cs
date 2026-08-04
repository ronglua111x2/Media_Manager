using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public static class RecipeRuntimeSettings
{
    public const string ParallelSearchCountKey = "parallelSearchCount";
    public const string MaxCandidatesPerFetchKey = "maxCandidatesPerFetch";
    public const string UseShowSnapshotSearchKey = "useShowSnapshotSearch";
    public const string SnapshotTargetResultsKey = "snapshotTargetResults";
    public const string MovieSearchTimeoutSecondsKey = "movieSearchTimeoutSeconds";
    public const string ParallelSearchTimeoutSecondsKey = "parallelSearchTimeoutSeconds";
    public const string SnapshotTimeoutSecondsKey = "snapshotTimeoutSeconds";
    public const string SnapshotIdleTimeoutSecondsKey = "snapshotIdleTimeoutSeconds";
    public const string EnableSearchPaginationKey = "enableSearchPagination";
    public const string PaginationPageSizeKey = "paginationPageSize";
    public const string PaginationMaxPagesMovieKey = "paginationMaxPagesMovie";
    public const string PaginationMaxPagesTvParallelKey = "paginationMaxPagesTvParallel";
    public const string PaginationMaxPagesTvSnapshotKey = "paginationMaxPagesTvSnapshot";
    public const string PaginationMaxTotalResultsKey = "paginationMaxTotalResults";
    public const string PaginationIdleTimeoutSecondsMovieKey = "paginationIdleTimeoutSecondsMovie";
    public const string PaginationIdleTimeoutSecondsTvParallelKey = "paginationIdleTimeoutSecondsTvParallel";
    public const string PaginationIdleTimeoutSecondsTvSnapshotKey = "paginationIdleTimeoutSecondsTvSnapshot";
    public const string SearchIdleTimeoutSecondsMovieKey = "searchIdleTimeoutSecondsMovie";
    public const string SearchIdleTimeoutSecondsTvParallelKey = "searchIdleTimeoutSecondsTvParallel";
    public const string LocalMatchWorkersKey = "localMatchWorkers";
    public const string DeduplicateCandidatesKey = "deduplicateCandidates";
    public const string FuzzyDeduplicateKey = "fuzzyDeduplicate";
    public const string FuzzyDeduplicateSizeToleranceMbKey = "fuzzyDeduplicateSizeToleranceMb";
    public const string EnableCandidateMetadataProbeKey = "enableCandidateMetadataProbe";
    public const string EpisodeNumberingModeKey = "episodeNumberingMode";
    public const string CustomQueryLegacyKey = "customQuery";
    public const string SkipDefaultTitleKey = "skipDefaultTitle";
    public const string SanitizeQueryKey = "sanitizeQuery";
    public const string UseLibraryEnglishTitlesKey = "useLibraryEnglishTitles";
    public const string MaxLibraryAlternativeTitlesForSearchKey = "maxLibraryAlternativeTitlesForSearch";
    public const int DefaultMaxLibraryAlternativeTitlesForSearch = 4;
    public const int MinMaxLibraryAlternativeTitlesForSearch = 0;
    public const int MaxMaxLibraryAlternativeTitlesForSearch = 16;
    public const string PackExtrasPriorityScoreKey = "packExtrasPriorityScore";
    public const string PackExtrasPriorityEnabledKey = "packExtrasPriorityEnabled";
    public const int DefaultPackExtrasPriorityScore = 2500;
    public const string QualityWeightKey = "qualityWeight";
    public const string AudioWeightKey = "audioWeight";
    public const string SeedersWeightKey = "seedersWeight";
    public const string SeedersCapKey = "seedersCap";
    public const string IdentityWeightKey = "identityWeight";
    public const string EpisodeWeightKey = "episodeWeight";
    public const string SizeWeightKey = "sizeWeight";
    public const string SizePreferenceKey = "sizePreference";
    public const string LegacyIdealSizeGbKey = "idealSizeGb";
    public const string LegacySizeBandGbKey = "sizeBandGb";
    public const string EnableCandidateDebugLogKey = "enableCandidateDebugLog";
    public const string SeasonMatchScorePerSeasonKey = "seasonMatchScorePerSeason";
    public const string SingleSeasonBoostKey = "singleSeasonBoost";
    public const string StandardTvEpisodeNumbering = "Standard TV";
    public const string AnimeAbsoluteEpisodeNumbering = "Anime absolute";
    public const int DefaultPaginationPageSize = 500;
    public const int MinPaginationPageSize = 50;
    public const int MaxPaginationPageSize = 1000;
    public const int DefaultPaginationMaxPagesMovie = 3;
    public const int DefaultPaginationMaxPagesTvParallel = 1;
    public const int DefaultPaginationMaxPagesTvSnapshot = 2;
    public const int MinPaginationMaxPages = 1;
    public const int MaxPaginationMaxPages = 10;
    public const int DefaultPaginationMaxTotalResults = 1500;
    public const int MinPaginationMaxTotalResults = 100;
    public const int MaxPaginationMaxTotalResults = 5000;
    public const int DefaultPaginationIdleTimeoutSecondsMovie = 10;
    public const int DefaultPaginationIdleTimeoutSecondsTvParallel = 6;
    public const int DefaultPaginationIdleTimeoutSecondsTvSnapshot = 8;
    public const int DefaultSearchIdleTimeoutSecondsMovie = 10;
    public const int DefaultSearchIdleTimeoutSecondsTvParallel = 8;
    public const int MinSearchIdleTimeoutSeconds = 0;
    public const int MaxSearchIdleTimeoutSeconds = 120;

    public static int GetParallelSearchCount(SearchRecipe recipe, AutoTorrentSettings fallback) =>
        GetInt(GetSearchModule(recipe), ParallelSearchCountKey, fallback.MaxParallelSearches, 1, 8);

    public static int GetMaxCandidatesPerFetch(SearchRecipe recipe, AutoTorrentSettings fallback) =>
        GetInt(GetSearchModule(recipe), MaxCandidatesPerFetchKey, fallback.MaxCandidatesPerFetch, 1, 10);

    public static bool GetUseShowSnapshotSearch(SearchRecipe recipe, AutoTorrentSettings fallback) =>
        GetBool(GetSearchModule(recipe), UseShowSnapshotSearchKey, fallback.UseShowSnapshotSearch);

    public static int GetSnapshotTargetResults(SearchRecipe recipe, AutoTorrentSettings fallback) =>
        GetInt(GetSearchModule(recipe), SnapshotTargetResultsKey, fallback.SnapshotTargetResults, 100, 5000);

    public static int GetMovieSearchTimeoutSeconds(SearchRecipe recipe, AutoTorrentSettings fallback) =>
        GetInt(GetSearchModule(recipe), MovieSearchTimeoutSecondsKey, fallback.MovieSearchTimeoutSeconds, 10, 300);

    public static int GetParallelSearchTimeoutSeconds(SearchRecipe recipe, AutoTorrentSettings fallback) =>
        GetInt(GetSearchModule(recipe), ParallelSearchTimeoutSecondsKey, fallback.ParallelSearchTimeoutSeconds, 10, 300);

    public static int GetSnapshotTimeoutSeconds(SearchRecipe recipe, AutoTorrentSettings fallback) =>
        GetInt(GetSearchModule(recipe), SnapshotTimeoutSecondsKey, fallback.SnapshotTimeoutSeconds, 30, 300);

    public static int GetSnapshotIdleTimeoutSeconds(SearchRecipe recipe, AutoTorrentSettings fallback) =>
        GetInt(GetSearchModule(recipe), SnapshotIdleTimeoutSecondsKey, fallback.SnapshotIdleTimeoutSeconds, 0, 120);

    public static int GetLocalMatchWorkers(SearchRecipe recipe, AutoTorrentSettings fallback) =>
        GetInt(GetSearchModule(recipe), LocalMatchWorkersKey, fallback.LocalMatchWorkers, 1, 8);

    public static bool GetDeduplicateCandidates(SearchRecipe recipe, AutoTorrentSettings fallback) =>
        GetBool(GetSearchModule(recipe), DeduplicateCandidatesKey, fallback.DeduplicateCandidates);

    public static bool GetFuzzyDeduplicate(SearchRecipe recipe, AutoTorrentSettings fallback) =>
        GetBool(GetSearchModule(recipe), FuzzyDeduplicateKey, fallback.FuzzyDeduplicate);

    public static int GetFuzzyDeduplicateSizeToleranceMb(SearchRecipe recipe, AutoTorrentSettings fallback) =>
        GetInt(GetSearchModule(recipe), FuzzyDeduplicateSizeToleranceMbKey, fallback.FuzzyDeduplicateSizeToleranceMb, 0, 100);

    public static bool GetEnableCandidateMetadataProbe(SearchRecipe recipe, AutoTorrentSettings fallback) =>
        GetBool(GetParserModule(recipe), EnableCandidateMetadataProbeKey, fallback.EnableCandidateMetadataProbe);

    public static bool GetEnableCandidateDebugLog(SearchRecipe recipe) =>
        GetBool(GetSearchModule(recipe), EnableCandidateDebugLogKey, false);

    public static bool GetEnableSearchPagination(SearchRecipe recipe) =>
        GetBool(GetSearchModule(recipe), EnableSearchPaginationKey, recipe.TargetKind != MediaKind.TvEpisode);

    public static int GetPaginationPageSize(SearchRecipe recipe) =>
        GetInt(
            GetSearchModule(recipe),
            PaginationPageSizeKey,
            DefaultPaginationPageSize,
            MinPaginationPageSize,
            MaxPaginationPageSize);

    public static int GetPaginationMaxPagesMovie(SearchRecipe recipe) =>
        GetInt(
            GetSearchModule(recipe),
            PaginationMaxPagesMovieKey,
            DefaultPaginationMaxPagesMovie,
            MinPaginationMaxPages,
            MaxPaginationMaxPages);

    public static int GetPaginationMaxPagesTvParallel(SearchRecipe recipe) =>
        GetInt(
            GetSearchModule(recipe),
            PaginationMaxPagesTvParallelKey,
            DefaultPaginationMaxPagesTvParallel,
            MinPaginationMaxPages,
            MaxPaginationMaxPages);

    public static int GetPaginationMaxPagesTvSnapshot(SearchRecipe recipe) =>
        GetInt(
            GetSearchModule(recipe),
            PaginationMaxPagesTvSnapshotKey,
            DefaultPaginationMaxPagesTvSnapshot,
            MinPaginationMaxPages,
            MaxPaginationMaxPages);

    public static int GetPaginationMaxTotalResults(SearchRecipe recipe) =>
        GetInt(
            GetSearchModule(recipe),
            PaginationMaxTotalResultsKey,
            DefaultPaginationMaxTotalResults,
            MinPaginationMaxTotalResults,
            MaxPaginationMaxTotalResults);

    public static int GetPaginationIdleTimeoutSecondsMovie(SearchRecipe recipe) =>
        GetInt(
            GetSearchModule(recipe),
            PaginationIdleTimeoutSecondsMovieKey,
            DefaultPaginationIdleTimeoutSecondsMovie,
            MinSearchIdleTimeoutSeconds,
            MaxSearchIdleTimeoutSeconds);

    public static int GetPaginationIdleTimeoutSecondsTvParallel(SearchRecipe recipe) =>
        GetInt(
            GetSearchModule(recipe),
            PaginationIdleTimeoutSecondsTvParallelKey,
            DefaultPaginationIdleTimeoutSecondsTvParallel,
            MinSearchIdleTimeoutSeconds,
            MaxSearchIdleTimeoutSeconds);

    public static int GetPaginationIdleTimeoutSecondsTvSnapshot(SearchRecipe recipe) =>
        GetInt(
            GetSearchModule(recipe),
            PaginationIdleTimeoutSecondsTvSnapshotKey,
            DefaultPaginationIdleTimeoutSecondsTvSnapshot,
            MinSearchIdleTimeoutSeconds,
            MaxSearchIdleTimeoutSeconds);

    public static int GetSearchIdleTimeoutSecondsMovie(SearchRecipe recipe) =>
        GetInt(
            GetSearchModule(recipe),
            SearchIdleTimeoutSecondsMovieKey,
            DefaultSearchIdleTimeoutSecondsMovie,
            MinSearchIdleTimeoutSeconds,
            MaxSearchIdleTimeoutSeconds);

    public static int GetSearchIdleTimeoutSecondsTvParallel(SearchRecipe recipe) =>
        GetInt(
            GetSearchModule(recipe),
            SearchIdleTimeoutSecondsTvParallelKey,
            DefaultSearchIdleTimeoutSecondsTvParallel,
            MinSearchIdleTimeoutSeconds,
            MaxSearchIdleTimeoutSeconds);

    public static string GetEpisodeNumberingMode(SearchRecipe recipe) =>
        NormalizeEpisodeNumberingMode(GetString(GetParserModule(recipe), EpisodeNumberingModeKey, StandardTvEpisodeNumbering));

    public static bool UsesAnimeAbsoluteEpisodeNumbering(SearchRecipe recipe) =>
        string.Equals(GetEpisodeNumberingMode(recipe), AnimeAbsoluteEpisodeNumbering, StringComparison.OrdinalIgnoreCase);

    public static int GetPackExtrasPriorityScoreBoost(SearchRecipe recipe, string torrentName)
    {
        if (recipe.TargetKind != MediaKind.TvSeasonPack ||
            !PackExtrasPriorityScorer.ContainsExtrasKeywords(torrentName))
        {
            return 0;
        }

        var weights = GetCandidateScoringWeights(recipe);
        if (!weights.PackExtrasPriorityEnabled)
        {
            return 0;
        }

        return weights.PackExtrasPriorityScore;
    }

    public static CandidateScoringWeights GetCandidateScoringWeights(SearchRecipe recipe)
    {
        var module = GetScoringModule(recipe);
        if (module is null || !module.IsEnabled)
        {
            return CandidateScoringWeights.Default;
        }

        return GetCandidateScoringWeights(module);
    }

    public static CandidateScoringWeights GetCandidateScoringWeights(RecipeModuleConfig? scoringModule)
    {
        var defaults = CandidateScoringWeights.Default;
        if (scoringModule is null)
        {
            return defaults;
        }

        return new CandidateScoringWeights(
            QualityWeight: GetInt(scoringModule, QualityWeightKey, defaults.QualityWeight, 0, 50_000_000),
            AudioWeight: GetInt(scoringModule, AudioWeightKey, defaults.AudioWeight, 0, 10_000_000),
            SeedersWeight: GetInt(scoringModule, SeedersWeightKey, defaults.SeedersWeight, 0, 10_000),
            SeedersCap: GetInt(scoringModule, SeedersCapKey, defaults.SeedersCap, 0, 500_000),
            IdentityWeight: GetInt(scoringModule, IdentityWeightKey, defaults.IdentityWeight, 0, 10_000),
            EpisodeWeight: GetInt(scoringModule, EpisodeWeightKey, defaults.EpisodeWeight, 0, 100_000),
            SizeWeight: GetInt(scoringModule, SizeWeightKey, defaults.SizeWeight, 0, 10_000_000),
            SizePreference: GetSizePreferenceMode(scoringModule, defaults.SizePreference),
            SeasonMatchScorePerSeason: GetInt(scoringModule, SeasonMatchScorePerSeasonKey, defaults.SeasonMatchScorePerSeason, 0, 10_000),
            SingleSeasonBoost: GetInt(scoringModule, SingleSeasonBoostKey, defaults.SingleSeasonBoost, 0, 100_000),
            PackExtrasPriorityEnabled: GetPackExtrasPriorityEnabled(scoringModule),
            PackExtrasPriorityScore: GetInt(scoringModule, PackExtrasPriorityScoreKey, defaults.PackExtrasPriorityScore, 0, 50_000));
    }

    public static CandidateScoringWeights GetDefaultCandidateScoringWeights() => CandidateScoringWeights.Default;

    public static void ApplyScoringDefaults(RecipeModuleConfig scoringModule)
    {
        foreach (var key in ScoringExtensionKeys)
        {
            scoringModule.ExtensionData.Remove(key);
        }
    }

    public static IReadOnlyList<string> ScoringExtensionKeys { get; } =
    [
        QualityWeightKey,
        AudioWeightKey,
        SeedersWeightKey,
        SeedersCapKey,
        IdentityWeightKey,
        EpisodeWeightKey,
        SizeWeightKey,
        SizePreferenceKey,
        LegacyIdealSizeGbKey,
        LegacySizeBandGbKey,
        SeasonMatchScorePerSeasonKey,
        SingleSeasonBoostKey,
        PackExtrasPriorityEnabledKey,
        PackExtrasPriorityScoreKey
    ];

    public static IReadOnlyList<string> SizePreferenceOptions { get; } =
    [
        "Prefer larger",
        "Prefer smaller",
        "Off"
    ];

    public static SizePreferenceMode GetSizePreferenceMode(SearchRecipe recipe) =>
        GetSizePreferenceMode(GetScoringModule(recipe), CandidateScoringWeights.Default.SizePreference);

    public static string NormalizeSizePreferenceOption(string? value)
    {
        if (string.Equals(value, "prefer smaller", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "smaller", StringComparison.OrdinalIgnoreCase))
        {
            return "Prefer smaller";
        }

        if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
        {
            return "Off";
        }

        return "Prefer larger";
    }

    public static bool GetPackExtrasPriorityEnabled(RecipeModuleConfig? scoringModule) =>
        GetBool(scoringModule, PackExtrasPriorityEnabledKey, true);

    public static RecipeModuleConfig? GetScoringModule(SearchRecipe recipe) =>
        recipe.Modules.FirstOrDefault(module =>
            module.BlockType == RecipeBlockType.Scoring);

    public static string NormalizeEpisodeNumberingMode(string? value)
    {
        if (string.Equals(value, AnimeAbsoluteEpisodeNumbering, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "AnimeAbsolute", StringComparison.OrdinalIgnoreCase))
        {
            return AnimeAbsoluteEpisodeNumbering;
        }

        return StandardTvEpisodeNumbering;
    }

    private static RecipeModuleConfig? GetSearchModule(SearchRecipe recipe) =>
        recipe.Modules.FirstOrDefault(module => module.BlockType == RecipeBlockType.SearchSource);

    private static RecipeModuleConfig? GetParserModule(SearchRecipe recipe) =>
        recipe.Modules.FirstOrDefault(module => module.BlockType == RecipeBlockType.CandidateParser);

    private static int GetInt(RecipeModuleConfig? module, string key, int fallback, int min, int max)
    {
        if (module?.ExtensionData.TryGetValue(key, out var value) == true &&
            int.TryParse(value, out var parsed))
        {
            return Math.Clamp(parsed, min, max);
        }

        return Math.Clamp(fallback, min, max);
    }

    private static SizePreferenceMode GetSizePreferenceMode(RecipeModuleConfig? scoringModule, SizePreferenceMode fallback)
    {
        if (scoringModule?.ExtensionData.TryGetValue(SizePreferenceKey, out var rawValue) != true ||
            string.IsNullOrWhiteSpace(rawValue))
        {
            return fallback;
        }

        var normalized = NormalizeSizePreferenceOption(rawValue);
        return normalized switch
        {
            "Prefer smaller" => SizePreferenceMode.PreferSmaller,
            "Off" => SizePreferenceMode.Off,
            _ => SizePreferenceMode.PreferLarger
        };
    }

    public static bool GetSkipDefaultTitle(RecipeModuleConfig? queryModule) =>
        GetBool(queryModule, SkipDefaultTitleKey, false);

    public static bool GetSanitizeQuery(RecipeModuleConfig? queryModule) =>
        GetBool(queryModule, SanitizeQueryKey, true);

    public static bool GetUseLibraryEnglishTitles(RecipeModuleConfig? identityModule) =>
        GetBool(identityModule, UseLibraryEnglishTitlesKey, false);

    public static int GetMaxLibraryAlternativeTitlesForSearch(RecipeModuleConfig? identityModule) =>
        GetInt(
            identityModule,
            MaxLibraryAlternativeTitlesForSearchKey,
            DefaultMaxLibraryAlternativeTitlesForSearch,
            MinMaxLibraryAlternativeTitlesForSearch,
            MaxMaxLibraryAlternativeTitlesForSearch);

    private static bool GetBool(RecipeModuleConfig? module, string key, bool fallback)
    {
        if (module?.ExtensionData.TryGetValue(key, out var value) == true &&
            bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return fallback;
    }

    private static string GetString(RecipeModuleConfig? module, string key, string fallback)
    {
        return module?.ExtensionData.TryGetValue(key, out var value) == true && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : fallback;
    }
}
