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
        var padded = new List<string>();
        var animePadding = RecipeRuntimeSettings.UsesAnimeAbsoluteEpisodeNumbering(recipe) && episode.EpisodeNumber is >= 0 and < 10;

        foreach (var template in templates)
        {
            foreach (var title in TitlesFor(template, titles))
            foreach (var quality in QualitiesFor(template, qualities))
            {
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [QueryTokenCatalog.TitleId] = title,
                    [QueryTokenCatalog.YearId] = show.FirstAirYear?.ToString() ?? string.Empty,
                    [QueryTokenCatalog.SeasonId] = episode.SeasonNumber.ToString(),
                    [QueryTokenCatalog.SeasonPaddedId] = episode.SeasonNumber.ToString("00"),
                    [QueryTokenCatalog.EpisodeId] = episode.EpisodeNumber.ToString(),
                    [QueryTokenCatalog.EpisodePaddedId] = episode.EpisodeNumber.ToString("00"),
                    [QueryTokenCatalog.QualityId] = quality
                };
                queries.Add(QueryTemplateRenderer.Render(template, values));

                // Fansub releases write anime episodes zero-padded ("Kanata Kara - 06"); token search engines
                // do not match "6" against "06", so also try the padded number (native-search P4).
                if (animePadding && UsesToken(template, QueryTokenCatalog.EpisodeId))
                {
                    values[QueryTokenCatalog.EpisodeId] = episode.EpisodeNumber.ToString("00");
                    padded.Add(QueryTemplateRenderer.Render(template, values));
                }
            }
        }

        queries.AddRange(padded);
        return QueryTemplateRenderer.Normalize(queries, RecipeRuntimeSettings.GetSanitizeQuery(queryModule));
    }

    public IReadOnlyList<string> BuildSeasonPackQueries(SearchRecipe recipe, TrackedShow show, IReadOnlyList<int> seasons)
    {
        var queryModule = GetModule(recipe, RecipeBlockType.QueryBuilder);
        var templates = QueryTemplateSelection.SelectSearchPatterns(recipe, showLevelOnly: false)
            .Where(template => (UsesToken(template, QueryTokenCatalog.SeasonId) || UsesToken(template, QueryTokenCatalog.SeasonPaddedId)) &&
                               !UsesToken(template, QueryTokenCatalog.EpisodeId) &&
                               !UsesToken(template, QueryTokenCatalog.EpisodePaddedId))
            .ToList();
        if (templates.Count == 0)
        {
            templates = ["{title} S{season:00}", "{title} season {season}"];
        }

        // Keep the request count reasonable: primary title plus one alternative per season and template.
        var titles = ResolveTitles(recipe, show.Title, show.GetSearchableAlternativeTitles()).Take(2).ToList();
        var qualities = QueryTokenCatalog.GetQualityValues(queryModule);
        var queries = new List<string>();
        foreach (var season in seasons.Where(season => season > 0).Distinct().Order())
        foreach (var template in templates)
        foreach (var title in TitlesFor(template, titles))
        foreach (var quality in QualitiesFor(template, qualities))
        {
            queries.Add(QueryTemplateRenderer.Render(template, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [QueryTokenCatalog.TitleId] = title,
                [QueryTokenCatalog.YearId] = show.FirstAirYear?.ToString() ?? string.Empty,
                [QueryTokenCatalog.SeasonId] = season.ToString(),
                [QueryTokenCatalog.SeasonPaddedId] = season.ToString("00"),
                [QueryTokenCatalog.QualityId] = quality,
                ["audio"] = string.Empty
            }));
        }

        return QueryTemplateRenderer.Normalize(queries, RecipeRuntimeSettings.GetSanitizeQuery(queryModule));
    }

    private static bool UsesToken(string template, string tokenId) =>
        QueryTokenCatalog.Parse(template).Any(span => string.Equals(span.Id, tokenId, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<string> BuildMovieQueries(SearchRecipe recipe, TrackedMovie movie)
    {
        var queryModule = GetModule(recipe, RecipeBlockType.QueryBuilder);
        var templates = QueryTemplateSelection.SelectSearchPatterns(recipe, showLevelOnly: false);
        var titles = ResolveTitles(recipe, movie.Title, movie.GetSearchableAlternativeTitles());
        var qualities = QueryTokenCatalog.GetQualityValues(queryModule);
        var queries = new List<string>();
        var yearless = new List<string>();

        foreach (var template in templates)
        {
            foreach (var title in TitlesFor(template, titles))
            foreach (var quality in QualitiesFor(template, qualities))
            {
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [QueryTokenCatalog.TitleId] = title,
                    [QueryTokenCatalog.YearId] = movie.ReleaseYear?.ToString() ?? string.Empty,
                    [QueryTokenCatalog.QualityId] = quality
                };
                queries.Add(QueryTemplateRenderer.Render(template, values));

                // SRCH-11: TMDB's year is often not the year in release names ("Coherence 2014"
                // found 12 rows, "Coherence" 479 — 322 of them tagged 2013). Also search without it.
                if (movie.ReleaseYear is not null && template.Contains($"{{{QueryTokenCatalog.YearId}}}", StringComparison.OrdinalIgnoreCase))
                {
                    values[QueryTokenCatalog.YearId] = string.Empty;
                    yearless.Add(QueryTemplateRenderer.Render(template, values));
                }
            }
        }

        queries.AddRange(yearless);
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
