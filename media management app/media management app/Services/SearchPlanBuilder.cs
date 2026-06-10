using media_management_app.Models;

namespace media_management_app.Services;

public sealed class SearchPlanBuilder : ISearchPlanBuilder
{
    public IReadOnlyList<string> BuildEpisodeQueries(SearchRecipe recipe, TrackedShow show, TrackedEpisode episode)
    {
        var queryModule = GetModule(recipe, RecipeBlockType.QueryBuilder);
        var identityModule = GetModule(recipe, RecipeBlockType.Identity);
        var templates = queryModule?.QueryTemplates.Count > 0
            ? queryModule.QueryTemplates
            : ["{title} S{season:00}E{episode:00} {quality}", "{title} {year} S{season:00}E{episode:00}", "{title} {season}x{episode:00}"];
        templates = AppendCustomQueryTemplate(templates, queryModule).ToList();
        var titles = GetTitles(show.Title, identityModule).ToList();
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
        var identityModule = GetModule(recipe, RecipeBlockType.Identity);
        var templates = queryModule?.QueryTemplates.Count > 0
            ? queryModule.QueryTemplates
            : ["{title} {year} {quality}", "{title} {quality}", "{title} {year}"];
        templates = AppendCustomQueryTemplate(templates, queryModule).ToList();
        var titles = GetTitles(movie.Title, identityModule).ToList();
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

    private static RecipeModuleConfig? GetModule(SearchRecipe recipe, RecipeBlockType blockType)
    {
        return recipe.Modules.FirstOrDefault(module => module.BlockType == blockType && module.IsEnabled);
    }

    private static IEnumerable<string> GetTitles(string title, RecipeModuleConfig? identityModule)
    {
        yield return title;
        if (identityModule is null)
        {
            yield break;
        }

        foreach (var alias in identityModule.Aliases.Where(alias => !string.IsNullOrWhiteSpace(alias)))
        {
            yield return alias.Trim();
        }
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

    private static IEnumerable<string> AppendCustomQueryTemplate(
        IEnumerable<string> templates,
        RecipeModuleConfig? queryModule)
    {
        foreach (var template in templates)
        {
            yield return template;
        }

        if (queryModule?.ExtensionData.TryGetValue("customQuery", out var customQuery) == true &&
            !string.IsNullOrWhiteSpace(customQuery))
        {
            yield return customQuery.Trim();
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
        return queries
            .Select(query => string.Join(' ', query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
            .Where(query => !string.IsNullOrWhiteSpace(query))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
