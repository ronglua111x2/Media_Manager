using System.Text.RegularExpressions;
using media_management_app.Common;
using media_management_app.Services;

namespace media_management_app.Search;

/// <summary>
/// Reduces a recipe query to what native full-text indexers actually need (native-search P4).
/// Recipe templates multiply queries ("{title} {year} {quality}", "{title} {quality}", "{title} {year}"
/// × alternative titles = 15 for one movie), which a per-site rate limit cannot afford and which
/// full-text engines answer with the same rows anyway. Quality tokens are dropped; for movies the year is
/// dropped too (TMDB's year is often not the release-name year, and YearMatcher checks it afterwards).
/// Episode/season markers ("S03E05", "S03", "- 06") are kept.
/// </summary>
public static partial class NativeQueryText
{
    public static string Reduce(string query, MediaKind kind, int? movieYear = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var reduced = QualityTokens().Replace(query, " ");
        if (kind == MediaKind.Movie)
        {
            // Keep a leading number ("2001 A Space Odyssey", "1917"): only drop years after the title, and when the
            // release year is known only that year (±1): "Blade Runner 2049 2017" → "Blade Runner 2049" (NS-18).
            var withoutYear = QueryTemplateRenderer.CollapseSpaces(DropYears(reduced, movieYear));
            // Short or common titles ("Dune", "Heat", "Alien") drown in unrelated results without the year, so
            // their year-bearing form stays its own query (plan 08 §Q3); the plain title is searched as well.
            if (!IsShortTitle(withoutYear))
            {
                reduced = withoutYear;
            }
        }

        reduced = QueryTemplateRenderer.CollapseSpaces(reduced);
        if (kind == MediaKind.TvSeasonPack)
        {
            reduced = CanonicalPack(reduced);
        }

        return reduced.Length == 0 ? QueryTemplateRenderer.CollapseSpaces(query) : reduced;
    }

    /// <summary>
    /// Season pack queries collapse to two forms per title (audit 07d GOV-03): "Show S01 complete" and "Show S01 pack"
    /// become "Show S01", "Show season 1" and "Show 2013 season 1" become "Show season 1". The pack templates sent up
    /// to 12 queries per pack; on the bench the dropped forms found almost nothing the kept ones did not (only
    /// torrentdownloads, whose RSS matches any word). Identical texts then share the memo and the HTTP cache.
    /// The qBittorrent path keeps the recipe's queries unchanged.
    /// </summary>
    public static string CanonicalPack(string query)
    {
        var match = PackQuery().Match(query);
        if (!match.Success)
        {
            return query;
        }

        var title = match.Groups["title"].Value.Trim();
        return match.Groups["s"].Success
            ? $"{title} S{int.Parse(match.Groups["s"].Value, System.Globalization.CultureInfo.InvariantCulture):00}"
            : $"{title} season {int.Parse(match.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// The quality-bearing form of a query ("Oppenheimer 2023 1080p" → "Oppenheimer 1080p" for movies), or null
    /// when the query has no quality token. Paged HTML indexers get it in addition to the reduced query.
    /// </summary>
    public static string? Specific(string query, MediaKind kind, int? movieYear = null)
    {
        if (string.IsNullOrWhiteSpace(query) || !QualityTokens().IsMatch(query))
        {
            return null;
        }

        var specific = kind == MediaKind.Movie ? DropYears(query, movieYear) : query;
        specific = QueryTemplateRenderer.CollapseSpaces(specific);
        if (kind == MediaKind.TvSeasonPack)
        {
            // "Show S01 complete 1080p" → "Show S01 1080p": the same canonical forms as Reduce, plus the quality.
            var qualities = string.Join(" ", QualityTokens().Matches(specific).Select(token => token.Value));
            specific = QueryTemplateRenderer.CollapseSpaces($"{Reduce(query, kind)} {qualities}");
        }
        return string.Equals(specific, Reduce(query, kind, movieYear), StringComparison.OrdinalIgnoreCase) ? null : specific;
    }

    /// <summary>
    /// Years after the first word: with a known release year only that year ±1 (a year that is part of the title —
    /// "Blade Runner 2049", "Wonder Woman 1984", "Godzilla 2000" — stays); without it, only a final year.
    /// </summary>
    private static string DropYears(string text, int? movieYear)
    {
        if (movieYear is { } year)
        {
            // The exact year when the query has it ("Godzilla 2000 1999" keeps 2000); otherwise a year one off
            // (TMDB vs release-name year).
            var exact = year.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var hasExact = TrailingYears().Matches(text).Any(match => match.Value == exact);
            return TrailingYears().Replace(text, match =>
                int.TryParse(match.Value, out var value) && (hasExact ? value == year : Math.Abs(value - year) <= 1) ? " " : match.Value);
        }

        return FinalYear().Replace(text, string.Empty);
    }

    /// <summary>
    /// Specific queries for paged HTML indexers (plan 08): the query's own quality form, or for a movie query
    /// without one, "title + quality" for the recipe's two best allowed qualities (the qualities that make a
    /// candidate "Good"). Empty when nothing specific applies.
    /// </summary>
    public static IReadOnlyList<string> SpecificQueries(string query, MediaKind kind, IReadOnlyList<string>? recipeQualities, int? movieYear = null)
    {
        if (Specific(query, kind, movieYear) is { } own)
        {
            return [own];
        }

        if (kind != MediaKind.Movie || recipeQualities is not { Count: > 0 })
        {
            return [];
        }

        var reduced = Reduce(query, kind, movieYear);
        return recipeQualities
            .Where(quality => !string.IsNullOrWhiteSpace(quality))
            .Select(quality => quality.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(quality => media_management_app.Services.TorrentQuality.GetRank(quality))
            .Take(2)
            .Select(quality => $"{reduced} {quality}")
            .ToList();
    }

    /// <summary>A quality marker ("1080p", "4k", "x265"...) as used in recipe queries.</summary>
    public static bool IsQualityToken(string token) => QualityTokens().IsMatch(token) && QualityTokens().Match(token).Length == token.Length;

    /// <summary>A four-digit year (1900–2099).</summary>
    public static bool IsYearToken(string token) => token.Length == 4 && (token.StartsWith("19") || token.StartsWith("20")) && token.All(char.IsDigit);

    /// <summary>One word of at most 6 letters ("Dune", "Heat", "Alien", "Us"): a bare title search is too broad.</summary>
    public static bool IsShortTitle(string title)
    {
        var tokens = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length == 1 && tokens[0].Length <= 6 && !tokens[0].All(char.IsDigit);
    }

    [GeneratedRegex(@"\b(2160p|1440p|1080p|720p|576p|480p|4k|uhd|hdr|x264|x265|hevc)\b", RegexOptions.IgnoreCase)]
    private static partial Regex QualityTokens();

    [GeneratedRegex(@"(?<=\S\s)(19|20)\d{2}\b")]
    private static partial Regex TrailingYears();

    // "<title> [year] S01|season 1 [complete|pack|batch|full]" (the pack templates).
    [GeneratedRegex(@"^(?<title>.+?)\s+(?:(?:19|20)\d{2}\s+)?(?:S(?<s>\d{1,3})|season\s+(?<n>\d{1,3}))(?:\s+(?:complete|pack|batch|full))*$", RegexOptions.IgnoreCase)]
    private static partial Regex PackQuery();

    // The last year of the query, followed only by quality tokens ("Oppenheimer 2023 1080p").
    [GeneratedRegex(@"(?<=\S)\s+(19|20)\d{2}(?=(\s+(2160p|1440p|1080p|720p|576p|480p|4k|uhd|hdr|x264|x265|hevc))*\s*$)", RegexOptions.IgnoreCase)]
    private static partial Regex FinalYear();
}
