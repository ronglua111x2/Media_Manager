using System.Text.RegularExpressions;

namespace media_management_app.Services;

public static class QueryTemplateRenderer
{
    private static readonly Regex NonSearchSafeCharacters = new(@"[^\p{L}\p{N}\s-]", RegexOptions.Compiled);

    public static string Render(string template, IReadOnlyDictionary<string, string> values)
    {
        var rendered = template;
        foreach (var pair in values)
        {
            rendered = rendered.Replace($"{{{pair.Key}}}", pair.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        return CollapseSpaces(rendered);
    }

    public static string CollapseSpaces(string query)
    {
        return string.Join(' ', query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    public static string Sanitize(string query)
    {
        var sanitized = NonSearchSafeCharacters.Replace(query, " ");
        return CollapseSpaces(sanitized);
    }

    public static IReadOnlyList<string> Normalize(IEnumerable<string> queries, bool sanitize)
    {
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var query in queries
                     .Select(query => sanitize ? Sanitize(query) : query)
                     .Select(CollapseSpaces)
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
