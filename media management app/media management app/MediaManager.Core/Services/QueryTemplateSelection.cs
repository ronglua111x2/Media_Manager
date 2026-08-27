using media_management_app.Models;

namespace media_management_app.Services;

internal static class QueryTemplateSelection
{
    public static IReadOnlyList<string> SelectSearchPatterns(
        SearchRecipe recipe,
        bool showLevelOnly,
        IReadOnlyList<string>? templatesOverride = null)
    {
        var queryModule = recipe.Modules.FirstOrDefault(module =>
            module.BlockType == RecipeBlockType.QueryBuilder && module.IsEnabled);
        var defaults = showLevelOnly
            ? QueryTokenCatalog.DefaultSnapshotTemplates
            : QueryTokenCatalog.DefaultTemplates(recipe.TargetKind);

        var stored = QueryTokenCatalog.NormalizeTemplates(templatesOverride ?? queryModule?.QueryTemplates);
        if (stored.Count == 0)
        {
            stored = defaults.ToList();
        }

        if (showLevelOnly)
        {
            stored = stored.Where(template => !QueryTokenCatalog.NeedsSeasonOrEpisodeContext(template)).ToList();
            if (stored.Count == 0)
            {
                stored = defaults.ToList();
            }
        }

        var accepted = stored
            .Where(template => QueryTokenCatalog.Validate(template, recipe.TargetKind).IsAccepted)
            .ToList();
        if (accepted.Count == 0)
        {
            accepted = defaults
                .Where(template => QueryTokenCatalog.Validate(template, recipe.TargetKind).IsAccepted)
                .ToList();
        }

        foreach (var customQuery in EnumerateCustomQueries(queryModule))
        {
            if (QueryTokenCatalog.Validate(customQuery, recipe.TargetKind).IsAccepted)
            {
                accepted.Add(customQuery);
            }
        }

        return accepted;
    }

    public static IReadOnlyList<string> DescribeRejected(
        SearchRecipe recipe,
        IReadOnlyList<string>? templatesOverride = null)
    {
        var queryModule = recipe.Modules.FirstOrDefault(module =>
            module.BlockType == RecipeBlockType.QueryBuilder && module.IsEnabled);
        var reasons = new List<string>();
        var patterns = (templatesOverride ?? queryModule?.QueryTemplates ?? [])
            .Concat(EnumerateCustomQueries(queryModule));
        foreach (var pattern in patterns.Where(pattern => !string.IsNullOrWhiteSpace(pattern)))
        {
            var result = QueryTokenCatalog.Validate(pattern.Trim(), recipe.TargetKind);
            if (!result.IsAccepted && !string.IsNullOrWhiteSpace(result.Reason))
            {
                reasons.Add($"{pattern.Trim()}: {result.Reason}");
            }
        }

        return reasons;
    }

    private static IEnumerable<string> EnumerateCustomQueries(RecipeModuleConfig? queryModule)
    {
        if (queryModule is null)
        {
            yield break;
        }

        foreach (var customQuery in queryModule.CustomQueries.Where(query => !string.IsNullOrWhiteSpace(query)))
        {
            yield return customQuery.Trim();
        }

        if (queryModule.CustomQueries.Count == 0 &&
            queryModule.ExtensionData.TryGetValue(RecipeRuntimeSettings.CustomQueryLegacyKey, out var legacyCustomQuery) &&
            !string.IsNullOrWhiteSpace(legacyCustomQuery))
        {
            yield return legacyCustomQuery.Trim();
        }
    }
}
