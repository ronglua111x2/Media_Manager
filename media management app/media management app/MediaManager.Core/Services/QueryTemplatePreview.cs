using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class QueryPreviewContext
{
    public string Title { get; init; } = string.Empty;

    public string Year { get; init; } = string.Empty;

    public string Season { get; init; } = string.Empty;

    public string SeasonPadded { get; init; } = string.Empty;

    public string Episode { get; init; } = string.Empty;

    public string EpisodePadded { get; init; } = string.Empty;

    public string Quality { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, string> ToValues() =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [QueryTokenCatalog.TitleId] = Title,
            [QueryTokenCatalog.YearId] = Year,
            [QueryTokenCatalog.SeasonId] = Season,
            [QueryTokenCatalog.SeasonPaddedId] = SeasonPadded,
            [QueryTokenCatalog.EpisodeId] = Episode,
            [QueryTokenCatalog.EpisodePaddedId] = EpisodePadded,
            [QueryTokenCatalog.QualityId] = Quality
        };
}

public sealed class QueryTemplatePreviewLine
{
    public required string Text { get; init; }

    public bool IsPairStart { get; init; }
}

public sealed class QueryTemplatePreviewResult
{
    public bool IsRejected { get; init; }

    public string? RejectedTokenId { get; init; }

    public string? Reason { get; init; }

    public string? Rendered { get; init; }

    public string? Sanitized { get; init; }

    public IReadOnlyList<string> ExpansionSample { get; init; } = [];

    public IReadOnlyList<QueryTokenSpan> Spans { get; init; } = [];
}

public static class QueryTemplatePreview
{
    public static QueryPreviewContext CreateSampleContext(MediaKind targetKind)
    {
        var samples = QueryTokenCatalog.CreateSampleValues();
        return FromValues(samples, targetKind);
    }

    public static QueryPreviewContext CreateContext(SearchRecipe recipe, string? titleOverride = null)
    {
        var context = CreateSampleContext(recipe.TargetKind);
        var queryModule = recipe.Modules.FirstOrDefault(module =>
            module.BlockType == RecipeBlockType.QueryBuilder);
        var firstQuality = QueryTokenCatalog.GetQualityValues(queryModule)
            .FirstOrDefault(quality => !string.IsNullOrWhiteSpace(quality));
        var title = !string.IsNullOrWhiteSpace(titleOverride)
            ? titleOverride.Trim()
            : string.IsNullOrWhiteSpace(recipe.Name) ? context.Title : recipe.Name.Trim();
        return new QueryPreviewContext
        {
            Title = title,
            Year = context.Year,
            Season = context.Season,
            SeasonPadded = context.SeasonPadded,
            Episode = context.Episode,
            EpisodePadded = context.EpisodePadded,
            Quality = firstQuality ?? context.Quality
        };
    }

    public static QueryTemplatePreviewResult Preview(string template, SearchRecipe recipe)
    {
        var queryModule = recipe.Modules.FirstOrDefault(module =>
            module.BlockType == RecipeBlockType.QueryBuilder);
        return Preview(
            template,
            recipe.TargetKind,
            CreateContext(recipe),
            RecipeRuntimeSettings.GetSanitizeQuery(queryModule),
            QueryTokenCatalog.GetQualityValues(queryModule));
    }

    public static IReadOnlyList<string> PreviewValidExpansions(
        SearchRecipe recipe,
        string title,
        IReadOnlyList<string> templates)
    {
        var queryModule = recipe.Modules.FirstOrDefault(module =>
            module.BlockType == RecipeBlockType.QueryBuilder);
        var context = CreateContext(recipe, title);
        var sanitize = RecipeRuntimeSettings.GetSanitizeQuery(queryModule);
        var qualities = QueryTokenCatalog.GetQualityValues(queryModule);
        var lines = new List<string>();
        foreach (var template in templates.Where(pattern => !string.IsNullOrWhiteSpace(pattern)))
        {
            var result = Preview(template.Trim(), recipe.TargetKind, context, sanitize, qualities);
            if (!result.IsRejected)
            {
                lines.AddRange(result.ExpansionSample);
            }
        }

        return lines;
    }

    public static IReadOnlyList<QueryTemplatePreviewLine> PreviewShortLongByTemplate(
        SearchRecipe recipe,
        IReadOnlyList<string> templates)
    {
        var queryModule = recipe.Modules.FirstOrDefault(module =>
            module.BlockType == RecipeBlockType.QueryBuilder);
        var sanitize = RecipeRuntimeSettings.GetSanitizeQuery(queryModule);
        var qualities = QueryTokenCatalog.GetQualityValues(queryModule);
        var shortTitle = QueryPreviewSamples.ShortTitle(recipe.TargetKind);
        var longTitle = QueryPreviewSamples.LongTitle(recipe.TargetKind);
        var lines = new List<QueryTemplatePreviewLine>();
        foreach (var template in templates.Where(pattern => !string.IsNullOrWhiteSpace(pattern)))
        {
            var trimmed = template.Trim();
            var shortLine = FirstSample(Preview(
                trimmed,
                recipe.TargetKind,
                CreateContext(recipe, shortTitle),
                sanitize,
                qualities));
            if (shortLine is null)
            {
                continue;
            }

            var longLine = FirstSample(Preview(
                trimmed,
                recipe.TargetKind,
                CreateContext(recipe, longTitle),
                sanitize,
                qualities));
            if (longLine is null)
            {
                continue;
            }

            lines.Add(new QueryTemplatePreviewLine { Text = shortLine, IsPairStart = true });
            lines.Add(new QueryTemplatePreviewLine { Text = longLine, IsPairStart = false });
        }

        return lines;
    }

    public static QueryTemplatePreviewResult Preview(
        string template,
        MediaKind targetKind,
        QueryPreviewContext context,
        bool sanitize = false,
        IReadOnlyList<string>? qualities = null)
    {
        var validation = QueryTokenCatalog.Validate(template, targetKind);
        if (!validation.IsAccepted)
        {
            return new QueryTemplatePreviewResult
            {
                IsRejected = true,
                RejectedTokenId = validation.RejectedTokenId,
                Reason = validation.Reason,
                Spans = validation.Spans
            };
        }

        var rendered = QueryTemplateRenderer.Render(template, context.ToValues());
        var qualityValues = qualities is { Count: > 0 }
            ? qualities
            : [string.IsNullOrWhiteSpace(context.Quality) ? string.Empty : context.Quality];
        var expansion = ExpandOneTitle(template, context, qualityValues, sanitize);

        return new QueryTemplatePreviewResult
        {
            Rendered = rendered,
            Sanitized = QueryTemplateRenderer.Sanitize(rendered),
            ExpansionSample = expansion,
            Spans = validation.Spans
        };
    }

    private static IReadOnlyList<string> ExpandOneTitle(
        string template,
        QueryPreviewContext context,
        IReadOnlyList<string> qualities,
        bool sanitize)
    {
        var qualitiesToUse = QueryTokenCatalog.HasExpansion(template, QueryTokenExpansion.Quality)
            ? qualities
            : [string.Empty];
        var queries = new List<string>();
        foreach (var quality in qualitiesToUse)
        {
            var values = context.ToValues().ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            values[QueryTokenCatalog.QualityId] = quality;
            queries.Add(QueryTemplateRenderer.Render(template, values));
        }

        return QueryTemplateRenderer.Normalize(queries, sanitize);
    }

    private static QueryPreviewContext FromValues(IReadOnlyDictionary<string, string> samples, MediaKind targetKind)
    {
        _ = targetKind;
        return new QueryPreviewContext
        {
            Title = Get(samples, QueryTokenCatalog.TitleId),
            Year = Get(samples, QueryTokenCatalog.YearId),
            Season = Get(samples, QueryTokenCatalog.SeasonId),
            SeasonPadded = Get(samples, QueryTokenCatalog.SeasonPaddedId),
            Episode = Get(samples, QueryTokenCatalog.EpisodeId),
            EpisodePadded = Get(samples, QueryTokenCatalog.EpisodePaddedId),
            Quality = Get(samples, QueryTokenCatalog.QualityId)
        };
    }

    private static string Get(IReadOnlyDictionary<string, string> samples, string id) =>
        samples.TryGetValue(id, out var value) ? value : string.Empty;

    private static string? FirstSample(QueryTemplatePreviewResult result)
    {
        if (result.IsRejected)
        {
            return null;
        }

        if (result.ExpansionSample.Count > 0)
        {
            return result.ExpansionSample[0];
        }

        return result.Sanitized ?? result.Rendered;
    }
}
