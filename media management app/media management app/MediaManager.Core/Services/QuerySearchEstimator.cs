using media_management_app.Models;

namespace media_management_app.Services;

public sealed class QuerySearchEstimate
{
    public int AcceptedCount { get; init; }

    public int RejectedCount { get; init; }

    public int SearchesPerTitle { get; init; }

    public int TitleBudget { get; init; }

    public int EstimatedMaxSearches { get; init; }

    public IReadOnlyList<string> RejectedReasons { get; init; } = [];
}

public static class QuerySearchEstimator
{
    public static QuerySearchEstimate Estimate(
        SearchRecipe recipe,
        IReadOnlyList<string>? templatesOverride = null)
    {
        var queryModule = recipe.Modules.FirstOrDefault(module =>
            module.BlockType == RecipeBlockType.QueryBuilder && module.IsEnabled);
        var qualities = QueryTokenCatalog.GetQualityValues(queryModule);
        var qualityCount = Math.Max(1, qualities.Count(quality => !string.IsNullOrWhiteSpace(quality)));
        if (qualities.Count == 1 && string.IsNullOrWhiteSpace(qualities[0]))
        {
            qualityCount = 1;
        }

        var titleBudget = GetTitleBudget(recipe);
        var patterns = QueryTemplateSelection.SelectSearchPatterns(recipe, showLevelOnly: false, templatesOverride);
        var rejected = QueryTemplateSelection.DescribeRejected(recipe, templatesOverride);
        var searchesPerTitle = 0;
        var estimatedMax = 0;

        foreach (var pattern in patterns)
        {
            var qualityFactor = QueryTokenCatalog.HasExpansion(pattern, QueryTokenExpansion.Quality)
                ? qualityCount
                : 1;
            var titleFactor = QueryTokenCatalog.HasExpansion(pattern, QueryTokenExpansion.Title)
                ? titleBudget
                : 1;
            searchesPerTitle += qualityFactor;
            estimatedMax += qualityFactor * titleFactor;
        }

        return new QuerySearchEstimate
        {
            AcceptedCount = patterns.Count,
            RejectedCount = rejected.Count,
            SearchesPerTitle = searchesPerTitle,
            TitleBudget = titleBudget,
            EstimatedMaxSearches = estimatedMax,
            RejectedReasons = rejected
        };
    }

    public static string FormatHuntEstimate(QuerySearchEstimate estimate)
    {
        var searches = estimate.EstimatedMaxSearches == 1
            ? "Up to 1 search per hunt"
            : $"Up to {estimate.EstimatedMaxSearches} searches per hunt";
        if (estimate.RejectedCount <= 0)
        {
            return searches;
        }

        var skipped = estimate.RejectedCount == 1
            ? "1 template skipped"
            : $"{estimate.RejectedCount} templates skipped";
        return $"{searches} · {skipped}";
    }

    public static int GetTitleBudget(SearchRecipe recipe)
    {
        var identity = recipe.Modules.FirstOrDefault(module => module.BlockType == RecipeBlockType.Identity);
        var query = recipe.Modules.FirstOrDefault(module => module.BlockType == RecipeBlockType.QueryBuilder);
        if (identity?.IsEnabled != true)
        {
            return 1;
        }

        var primary = RecipeRuntimeSettings.GetSkipDefaultTitle(query) ? 0 : 1;
        var aliases = identity.Aliases.Count(alias => !string.IsNullOrWhiteSpace(alias));
        var libraryCap = RecipeRuntimeSettings.GetUseLibraryEnglishTitles(identity)
            ? RecipeRuntimeSettings.GetMaxLibraryAlternativeTitlesForSearch(identity)
            : 0;
        return Math.Max(1, primary + aliases + libraryCap);
    }
}
