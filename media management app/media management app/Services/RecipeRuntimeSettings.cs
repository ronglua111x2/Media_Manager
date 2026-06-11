using media_management_app.Models;

namespace media_management_app.Services;

public static class RecipeRuntimeSettings
{
    public const string ParallelSearchCountKey = "parallelSearchCount";
    public const string MaxCandidatesPerFetchKey = "maxCandidatesPerFetch";
    public const string UseShowSnapshotSearchKey = "useShowSnapshotSearch";
    public const string SnapshotTargetResultsKey = "snapshotTargetResults";
    public const string SnapshotTimeoutSecondsKey = "snapshotTimeoutSeconds";
    public const string SnapshotIdleTimeoutSecondsKey = "snapshotIdleTimeoutSeconds";
    public const string LocalMatchWorkersKey = "localMatchWorkers";
    public const string DeduplicateCandidatesKey = "deduplicateCandidates";
    public const string FuzzyDeduplicateKey = "fuzzyDeduplicate";
    public const string FuzzyDeduplicateSizeToleranceMbKey = "fuzzyDeduplicateSizeToleranceMb";
    public const string EnableCandidateMetadataProbeKey = "enableCandidateMetadataProbe";

    public static int GetParallelSearchCount(SearchRecipe recipe, AutoTorrentSettings fallback) =>
        GetInt(GetSearchModule(recipe), ParallelSearchCountKey, fallback.MaxParallelSearches, 1, 8);

    public static int GetMaxCandidatesPerFetch(SearchRecipe recipe, AutoTorrentSettings fallback) =>
        GetInt(GetSearchModule(recipe), MaxCandidatesPerFetchKey, fallback.MaxCandidatesPerFetch, 1, 10);

    public static bool GetUseShowSnapshotSearch(SearchRecipe recipe, AutoTorrentSettings fallback) =>
        GetBool(GetSearchModule(recipe), UseShowSnapshotSearchKey, fallback.UseShowSnapshotSearch);

    public static int GetSnapshotTargetResults(SearchRecipe recipe, AutoTorrentSettings fallback) =>
        GetInt(GetSearchModule(recipe), SnapshotTargetResultsKey, fallback.SnapshotTargetResults, 100, 5000);

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

    private static bool GetBool(RecipeModuleConfig? module, string key, bool fallback)
    {
        if (module?.ExtensionData.TryGetValue(key, out var value) == true &&
            bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return fallback;
    }
}
