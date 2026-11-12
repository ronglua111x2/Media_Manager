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
    public static string Reduce(string query, MediaKind kind)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var reduced = QualityTokens().Replace(query, " ");
        if (kind == MediaKind.Movie)
        {
            // Keep a leading number ("2001 A Space Odyssey", "1917"): only drop years after the title.
            var withoutYear = QueryTemplateRenderer.CollapseSpaces(TrailingYears().Replace(reduced, " "));
            // Short or common titles ("Dune", "Heat", "Alien") drown in unrelated results without the year, so
            // their year-bearing form stays its own query (plan 08 §Q3); the plain title is searched as well.
            if (!IsShortTitle(withoutYear))
            {
                reduced = withoutYear;
            }
        }

        reduced = QueryTemplateRenderer.CollapseSpaces(reduced);
        return reduced.Length == 0 ? QueryTemplateRenderer.CollapseSpaces(query) : reduced;
    }

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
}
