using media_management_app.Models;

namespace media_management_app.Services;

public sealed class SearchPlanBuilder : ISearchPlanBuilder
{
    private readonly ISearchTitleResolver _titleResolver;

    public SearchPlanBuilder(ISearchTitleResolver titleResolver)
    {
        _titleResolver = titleResolver;
    }

    public IReadOnlyList<string> BuildEpisodeQueries(SearchRecipe recipe, TrackedShow show, TrackedEpisode episode)
    {
        var queryModule = GetModule(recipe, RecipeBlockType.QueryBuilder);
        var templates = queryModule?.QueryTemplates.Count > 0
            ? queryModule.QueryTemplates
            : ["{title} S{season:00}E{episode:00} {quality}", "{title} {year} S{season:00}E{episode:00}", "{title} {season}x{episode:00}"];
        templates = AppendCustomQueries(templates, queryModule).ToList();
        var titles = ResolveTitles(recipe, show.Title, show.GetSearchableAlternativeTitles());
        var qualities = GetQualities(queryModule).ToList();
        var audio = queryModule?.PreferredAudioCodec ?? string.Empty;
        var queries = new List<string>();

        foreach (var template in templates)
        foreach (var title in titles)
        foreach (var quality in qualities)
        {
            queries.Add(Render(template, new Dictionary<string, string>
            {
                ["title"] = title,
                ["year"] = show.FirstAirYear?.ToString() ?? string.Empty,
                ["season"] = episode.SeasonNumber.ToString(),
                ["season:00"] = episode.SeasonNumber.ToString("00"),
                ["episode"] = episode.EpisodeNumber.ToString(),
                ["episode:00"] = episode.EpisodeNumber.ToString("00"),
                ["quality"] = quality,
                ["audio"] = audio
            }));
        }

        return NormalizeQueries(queries);
    }

    public IReadOnlyList<string> BuildMovieQueries(SearchRecipe recipe, TrackedMovie movie)
    {
        var queryModule = GetModule(recipe, RecipeBlockType.QueryBuilder);
        var templates = queryModule?.QueryTemplates.Count > 0
            ? queryModule.QueryTemplates
            : ["{title} {year} {quality}", "{title} {quality}", "{title} {year}"];
        templates = AppendCustomQueries(templates, queryModule).ToList();
        var titles = ResolveTitles(recipe, movie.Title, movie.GetSearchableAlternativeTitles());
        var qualities = GetQualities(queryModule).ToList();
        var audio = queryModule?.PreferredAudioCodec ?? string.Empty;
        var queries = new List<string>();

        foreach (var template in templates)
        foreach (var title in titles)
        foreach (var quality in qualities)
        {
            queries.Add(Render(template, new Dictionary<string, string>
            {
                ["title"] = title,
                ["year"] = movie.ReleaseYear?.ToString() ?? string.Empty,
                ["quality"] = quality,
                ["audio"] = audio
            }));
        }

        return NormalizeQueries(queries);
    }

    public IReadOnlyList<string> BuildShowSnapshotQueries(SearchRecipe recipe, TrackedShow show)
    {
        var queryModule = GetModule(recipe, RecipeBlockType.QueryBuilder);
        var templates = queryModule?.QueryTemplates.Count > 0
            ? queryModule.QueryTemplates.Where(IsShowLevelTemplate).ToList()
            : ["{title} {year}", "{title} {quality}", "{title}"];
        if (templates.Count == 0)
        {
            templates = ["{title} {year}", "{title} {quality}", "{title}"];
        }

        templates = AppendCustomQueries(templates, queryModule).ToList();
        var titles = ResolveTitles(recipe, show.Title, show.GetSearchableAlternativeTitles());
        var qualities = GetQualities(queryModule).ToList();
        var audio = queryModule?.PreferredAudioCodec ?? string.Empty;
        var queries = new List<string>();

        foreach (var template in templates)
        foreach (var title in titles)
        foreach (var quality in qualities)
        {
            queries.Add(Render(template, new Dictionary<string, string>
            {
                ["title"] = title,
                ["year"] = show.FirstAirYear?.ToString() ?? string.Empty,
                ["quality"] = quality,
                ["audio"] = audio
            }));
        }

        return NormalizeQueries(queries);
    }

    private IReadOnlyList<string> ResolveTitles(
        SearchRecipe recipe,
        string primaryTitle,
        IReadOnlyList<string> libraryAlternativeTitles)
    {
        return _titleResolver.Resolve(
            _titleResolver.CreateRequest(recipe, primaryTitle, libraryAlternativeTitles));
    }

    private static bool IsShowLevelTemplate(string template)
    {
        return !template.Contains("{season", StringComparison.OrdinalIgnoreCase) &&
               !template.Contains("{episode", StringComparison.OrdinalIgnoreCase);
    }

    private static RecipeModuleConfig? GetModule(SearchRecipe recipe, RecipeBlockType blockType)
    {
        return recipe.Modules.FirstOrDefault(module => module.BlockType == blockType && module.IsEnabled);
    }

    private static IEnumerable<string> GetQualities(RecipeModuleConfig? queryModule)
    {
        var qualities = queryModule?.QualityAllowList
            .Where(quality => !string.IsNullOrWhiteSpace(quality))
            .Select(quality => quality.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return qualities is { Count: > 0 } ? qualities : [string.Empty];
    }

    private static IEnumerable<string> AppendCustomQueries(
        IEnumerable<string> templates,
        RecipeModuleConfig? queryModule)
    {
        foreach (var template in templates)
        {
            yield return template;
        }

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

    private static string Render(string template, IReadOnlyDictionary<string, string> values)
    {
        var rendered = template;
        foreach (var pair in values)
        {
            rendered = rendered.Replace($"{{{pair.Key}}}", pair.Value, StringComparison.OrdinalIgnoreCase);
        }

        return string.Join(' ', rendered.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static IReadOnlyList<string> NormalizeQueries(IEnumerable<string> queries)
    {
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var query in queries
                     .Select(query => string.Join(' ', query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
                     .Where(query => !string.IsNullOrWhiteSpace(query)))
        {
            if (seenKeys.Add(EnglishAlternativeTitleFilter.NormalizeForSearchKey(query)))
            {
                result.Add(query);
            }
        }

        return result;
    }
}
