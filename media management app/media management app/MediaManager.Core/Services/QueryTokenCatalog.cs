using System.Text.RegularExpressions;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public enum QueryTokenExpansion
{
    None = 0,
    Title = 1,
    Quality = 2
}

public sealed class QueryTokenDefinition
{
    public required string Id { get; init; }

    public string Placeholder => "{" + Id + "}";

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    public required string SampleValue { get; init; }

    public bool IsEnabled { get; init; } = true;

    public required IReadOnlyList<MediaKind> ApplicableKinds { get; init; }

    public QueryTokenExpansion Expansion { get; init; }

    public bool RequiresSeasonOrEpisodeContext { get; init; }

    public string DisplayBrushKey { get; init; } = string.Empty;
}

public sealed class QueryTokenSpan
{
    public required string Id { get; init; }

    public int Start { get; init; }

    public int Length { get; init; }
}

public enum QueryTemplatePartKind
{
    Literal,
    ValidToken,
    InvalidToken
}

public sealed class QueryTemplateDisplayPart
{
    public required string Text { get; init; }

    public required QueryTemplatePartKind Kind { get; init; }

    public string? TokenId { get; init; }

    public string? DisplayBrushKey { get; init; }
}

public sealed class QueryTemplateValidationResult
{
    public bool IsAccepted { get; init; }

    public string? RejectedTokenId { get; init; }

    public string? Reason { get; init; }

    public IReadOnlyList<QueryTokenSpan> Spans { get; init; } = [];
}

public static class QueryTokenCatalog
{
    public const string TitleId = "title";
    public const string YearId = "year";
    public const string SeasonId = "season";
    public const string SeasonPaddedId = "season:00";
    public const string EpisodeId = "episode";
    public const string EpisodePaddedId = "episode:00";
    public const string QualityId = "quality";
    public const string AudioId = "audio";

    public const string TitleBrushKey = "AppBrushShowAccent";
    public const string YearBrushKey = "AppBrushMovieAccent";
    public const string SeasonBrushKey = "AppBrushWatchWatching";
    public const string EpisodeBrushKey = "AppBrushJellyfinBrand";
    public const string QualityBrushKey = "AppBrushAccent";
    public const string InvalidBrushKey = "AppBrushWarning";
    public const string LiteralBrushKey = "AppBrushText";

    public const int MaxTemplates = 10;

    private static readonly MediaKind[] AllKinds =
    [
        MediaKind.TvEpisode,
        MediaKind.TvSeasonPack,
        MediaKind.Movie
    ];

    private static readonly MediaKind[] TvKinds =
    [
        MediaKind.TvEpisode,
        MediaKind.TvSeasonPack
    ];

    private static readonly MediaKind[] TvEpisodeKind =
    [
        MediaKind.TvEpisode
    ];

    private static readonly Regex PlaceholderRegex = new(@"\{([^{}]+)\}", RegexOptions.Compiled);

    public static IReadOnlyList<QueryTokenDefinition> All { get; } =
    [
        new()
        {
            Id = TitleId,
            DisplayName = "Title",
            Description = "Resolved show or movie title, including aliases when Identity is enabled.",
            SampleValue = "Better Call Saul",
            ApplicableKinds = AllKinds,
            Expansion = QueryTokenExpansion.Title,
            DisplayBrushKey = TitleBrushKey
        },
        new()
        {
            Id = YearId,
            DisplayName = "Year",
            Description = "First air year or movie release year.",
            SampleValue = "2015",
            ApplicableKinds = AllKinds,
            DisplayBrushKey = YearBrushKey
        },
        new()
        {
            Id = SeasonId,
            DisplayName = "Season",
            Description = "Season number without padding.",
            SampleValue = "1",
            ApplicableKinds = TvKinds,
            RequiresSeasonOrEpisodeContext = true,
            DisplayBrushKey = SeasonBrushKey
        },
        new()
        {
            Id = SeasonPaddedId,
            DisplayName = "Season 01",
            Description = "Season number zero-padded to two digits.",
            SampleValue = "01",
            ApplicableKinds = TvKinds,
            RequiresSeasonOrEpisodeContext = true,
            DisplayBrushKey = SeasonBrushKey
        },
        new()
        {
            Id = EpisodeId,
            DisplayName = "Episode",
            Description = "Episode number without padding.",
            SampleValue = "1",
            ApplicableKinds = TvEpisodeKind,
            RequiresSeasonOrEpisodeContext = true,
            DisplayBrushKey = EpisodeBrushKey
        },
        new()
        {
            Id = EpisodePaddedId,
            DisplayName = "Episode 01",
            Description = "Episode number zero-padded to two digits.",
            SampleValue = "01",
            ApplicableKinds = TvEpisodeKind,
            RequiresSeasonOrEpisodeContext = true,
            DisplayBrushKey = EpisodeBrushKey
        },
        new()
        {
            Id = QualityId,
            DisplayName = "Quality",
            Description = "Each quality from the Query module allow list.",
            SampleValue = "1080p",
            ApplicableKinds = AllKinds,
            Expansion = QueryTokenExpansion.Quality,
            DisplayBrushKey = QualityBrushKey
        },
        new()
        {
            Id = AudioId,
            DisplayName = "Audio",
            Description = "Retired. Audio preference belongs on the Quality filter, not in search queries.",
            SampleValue = string.Empty,
            IsEnabled = false,
            ApplicableKinds = AllKinds,
            DisplayBrushKey = InvalidBrushKey
        }
    ];

    public static IReadOnlyList<QueryTokenDefinition> Enabled { get; } =
        All.Where(token => token.IsEnabled).ToList();

    public static IReadOnlyList<string> DefaultEpisodeTemplates { get; } =
    [
        "{title} S{season:00}E{episode:00} {quality}",
        "{title} {year} S{season:00}E{episode:00} {quality}",
        "{title} {season}x{episode:00} {quality}",
        "{title} S{season:00}E{episode:00}"
    ];

    public static IReadOnlyList<string> DefaultPackTemplates { get; } =
    [
        "{title} S{season:00} complete {quality}",
        "{title} season {season} {quality}",
        "{title} S{season:00} pack {quality}",
        "{title} {year} season {season} {quality}"
    ];

    public static IReadOnlyList<string> DefaultMovieTemplates { get; } =
    [
        "{title} {year} {quality}",
        "{title} {quality}",
        "{title} {year}"
    ];

    public static IReadOnlyList<string> DefaultSnapshotTemplates { get; } =
    [
        "{title} {year}",
        "{title} {quality}",
        "{title}"
    ];

    public static List<string> NormalizeTemplates(IEnumerable<string>? templates)
    {
        return (templates ?? [])
            .Where(template => !string.IsNullOrWhiteSpace(template))
            .Select(template => template.Trim())
            .Take(MaxTemplates)
            .ToList();
    }

    public static QueryTokenDefinition? Find(string id) =>
        All.FirstOrDefault(token => string.Equals(token.Id, id, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> DefaultTemplates(MediaKind targetKind) =>
        targetKind switch
        {
            MediaKind.Movie => DefaultMovieTemplates,
            MediaKind.TvSeasonPack => DefaultPackTemplates,
            _ => DefaultEpisodeTemplates
        };

    public static IReadOnlyList<QueryTokenSpan> Parse(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return [];
        }

        return PlaceholderRegex.Matches(template)
            .Select(match => new QueryTokenSpan
            {
                Id = match.Groups[1].Value.Trim(),
                Start = match.Index,
                Length = match.Length
            })
            .Where(span => span.Id.Length > 0)
            .ToList();
    }

    public static IReadOnlyList<QueryTemplateDisplayPart> SplitDisplayParts(string? template, MediaKind targetKind)
    {
        if (string.IsNullOrEmpty(template))
        {
            return [];
        }

        var parts = new List<QueryTemplateDisplayPart>();
        var cursor = 0;
        foreach (var span in Parse(template))
        {
            if (span.Start > cursor)
            {
                parts.Add(new QueryTemplateDisplayPart
                {
                    Text = template[cursor..span.Start],
                    Kind = QueryTemplatePartKind.Literal
                });
            }

            var token = Find(span.Id);
            var isValid = token is not null &&
                          token.IsEnabled &&
                          token.ApplicableKinds.Contains(targetKind);
            parts.Add(new QueryTemplateDisplayPart
            {
                Text = template.Substring(span.Start, span.Length),
                Kind = isValid ? QueryTemplatePartKind.ValidToken : QueryTemplatePartKind.InvalidToken,
                TokenId = span.Id,
                DisplayBrushKey = isValid
                    ? token!.DisplayBrushKey
                    : InvalidBrushKey
            });
            cursor = span.Start + span.Length;
        }

        if (cursor < template.Length)
        {
            parts.Add(new QueryTemplateDisplayPart
            {
                Text = template[cursor..],
                Kind = QueryTemplatePartKind.Literal
            });
        }

        return parts;
    }

    public static QueryTemplateValidationResult Validate(string? template, MediaKind targetKind)
    {
        var spans = Parse(template);
        foreach (var span in spans)
        {
            var token = Find(span.Id);
            if (token is null)
            {
                return Rejected(spans, span.Id, $"unknown token {{{span.Id}}}");
            }

            if (!token.IsEnabled)
            {
                return Rejected(spans, token.Id, $"token {{{token.Id}}} was removed from query templates");
            }

            if (!token.ApplicableKinds.Contains(targetKind))
            {
                return Rejected(
                    spans,
                    token.Id,
                    $"token {{{token.Id}}} is not valid for {FormatKind(targetKind)} recipes");
            }
        }

        return new QueryTemplateValidationResult
        {
            IsAccepted = true,
            Spans = spans
        };
    }

    public static bool NeedsSeasonOrEpisodeContext(string? template) =>
        Parse(template).Any(span => Find(span.Id)?.RequiresSeasonOrEpisodeContext == true);

    public static bool HasExpansion(string? template, QueryTokenExpansion expansion) =>
        Parse(template).Any(span => Find(span.Id)?.Expansion == expansion);

    public static IReadOnlyList<string> GetQualityValues(RecipeModuleConfig? queryModule)
    {
        var qualities = queryModule?.QualityAllowList
            .Where(quality => !string.IsNullOrWhiteSpace(quality))
            .Select(quality => quality.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return qualities is { Count: > 0 } ? qualities : [string.Empty];
    }

    public static Dictionary<string, string> CreateSampleValues()
    {
        return Enabled.ToDictionary(
            token => token.Id,
            token => token.SampleValue,
            StringComparer.OrdinalIgnoreCase);
    }

    private static QueryTemplateValidationResult Rejected(
        IReadOnlyList<QueryTokenSpan> spans,
        string tokenId,
        string reason)
    {
        return new QueryTemplateValidationResult
        {
            IsAccepted = false,
            RejectedTokenId = tokenId,
            Reason = reason,
            Spans = spans
        };
    }

    private static string FormatKind(MediaKind targetKind) =>
        targetKind switch
        {
            MediaKind.Movie => "Movie",
            MediaKind.TvSeasonPack => "TV pack",
            MediaKind.TvEpisode => "TV episode",
            _ => targetKind.ToString()
        };
}
