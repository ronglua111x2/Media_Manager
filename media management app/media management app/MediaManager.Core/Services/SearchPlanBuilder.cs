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
        var templates = QueryTemplateSelection.SelectSearchPatterns(recipe, showLevelOnly: false);
        var titles = ResolveTitles(recipe, show.Title, show.GetSearchableAlternativeTitles());
        var qualities = QueryTokenCatalog.GetQualityValues(queryModule);
        var queries = new List<string>();

        foreach (var template in templates)
        {
            foreach (var title in TitlesFor(template, titles))
            foreach (var quality in QualitiesFor(template, qualities))
            {
                queries.Add(QueryTemplateRenderer.Render(template, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [QueryTokenCatalog.TitleId] = title,
                    [QueryTokenCatalog.YearId] = show.FirstAirYear?.ToString() ?? string.Empty,
                    [QueryTokenCatalog.SeasonId] = episode.SeasonNumber.ToString(),
                    [QueryTokenCatalog.SeasonPaddedId] = episode.SeasonNumber.ToString("00"),
                    [QueryTokenCatalog.EpisodeId] = episode.EpisodeNumber.ToString(),
                    [QueryTokenCatalog.EpisodePaddedId] = episode.EpisodeNumber.ToString("00"),
                    [QueryTokenCatalog.QualityId] = quality
                }));
            }
        }

        return QueryTemplateRenderer.Normalize(queries, RecipeRuntimeSettings.GetSanitizeQuery(queryModule));
    }

    public IReadOnlyList<string> BuildMovieQueries(SearchRecipe recipe, TrackedMovie movie)
    {
        var queryModule = GetModule(recipe, RecipeBlockType.QueryBuilder);
        var templates = QueryTemplateSelection.SelectSearchPatterns(recipe, showLevelOnly: false);
        var titles = ResolveTitles(recipe, movie.Title, movie.GetSearchableAlternativeTitles());
        var qualities = QueryTokenCatalog.GetQualityValues(queryModule);
        var queries = new List<string>();

        foreach (var template in templates)
        {
            foreach (var title in TitlesFor(template, titles))
            foreach (var quality in QualitiesFor(template, qualities))
            {
                queries.Add(QueryTemplateRenderer.Render(template, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [QueryTokenCatalog.TitleId] = title,
                    [QueryTokenCatalog.YearId] = movie.ReleaseYear?.ToString() ?? string.Empty,
                    [QueryTokenCatalog.QualityId] = quality
                }));
            }
        }

        return QueryTemplateRenderer.Normalize(queries, RecipeRuntimeSettings.GetSanitizeQuery(queryModule));
    }

    public IReadOnlyList<string> BuildShowSnapshotQueries(SearchRecipe recipe, TrackedShow show)
    {
        var queryModule = GetModule(recipe, RecipeBlockType.QueryBuilder);
        var templates = QueryTemplateSelection.SelectSearchPatterns(recipe, showLevelOnly: true);
        var titles = ResolveTitles(recipe, show.Title, show.GetSearchableAlternativeTitles());
        var qualities = QueryTokenCatalog.GetQualityValues(queryModule);
        var queries = new List<string>();

        foreach (var template in templates)
        {
            foreach (var title in TitlesFor(template, titles))
            foreach (var quality in QualitiesFor(template, qualities))
            {
                queries.Add(QueryTemplateRenderer.Render(template, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [QueryTokenCatalog.TitleId] = title,
                    [QueryTokenCatalog.YearId] = show.FirstAirYear?.ToString() ?? string.Empty,
                    [QueryTokenCatalog.QualityId] = quality
                }));
            }
        }

        return QueryTemplateRenderer.Normalize(queries, RecipeRuntimeSettings.GetSanitizeQuery(queryModule));
    }

    private IReadOnlyList<string> ResolveTitles(
        SearchRecipe recipe,
        string primaryTitle,
        IReadOnlyList<string> libraryAlternativeTitles)
    {
        return _titleResolver.Resolve(
            _titleResolver.CreateRequest(recipe, primaryTitle, libraryAlternativeTitles));
    }

    private static RecipeModuleConfig? GetModule(SearchRecipe recipe, RecipeBlockType blockType)
    {
        return recipe.Modules.FirstOrDefault(module => module.BlockType == blockType && module.IsEnabled);
    }

    private static IReadOnlyList<string> TitlesFor(string template, IReadOnlyList<string> titles) =>
        QueryTokenCatalog.HasExpansion(template, QueryTokenExpansion.Title) ? titles : [string.Empty];

    private static IReadOnlyList<string> QualitiesFor(string template, IReadOnlyList<string> qualities) =>
        QueryTokenCatalog.HasExpansion(template, QueryTokenExpansion.Quality) ? qualities : [string.Empty];
}
