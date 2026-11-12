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
            reduced = TrailingYears().Replace(reduced, " ");
        }

        reduced = QueryTemplateRenderer.CollapseSpaces(reduced);
        return reduced.Length == 0 ? QueryTemplateRenderer.CollapseSpaces(query) : reduced;
    }

    [GeneratedRegex(@"\b(2160p|1440p|1080p|720p|576p|480p|4k|uhd|hdr|x264|x265|hevc)\b", RegexOptions.IgnoreCase)]
    private static partial Regex QualityTokens();

    [GeneratedRegex(@"(?<=\S\s)(19|20)\d{2}\b")]
    private static partial Regex TrailingYears();
}
