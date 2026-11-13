namespace media_management_app.Services;

public static class QueryTemplateRenderer
{
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

    /// <summary>
    /// Primary sanitized search form (see <see cref="TitleNormalizer.ToSearchText"/>): diacritics folded,
    /// apostrophes joined ("Schindler's" → "Schindlers"), other punctuation turned into spaces.
    /// </summary>
    public static string Sanitize(string query) => TitleNormalizer.ToSearchText(query);

    /// <summary>
    /// Sanitizes and de-duplicates rendered queries. When <paramref name="sanitize"/> is on, every query
    /// also yields its <see cref="TitleNormalizer.QueryVariants"/>: the primary forms keep their original
    /// order and the extra variants (apostrophe split, accented spelling) are appended after all of them,
    /// so callers that stop early once they have enough candidates still try the main queries first.
    /// </summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string> queries, bool sanitize)
    {
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var primary = new List<string>();
        var extra = new List<string>();

        foreach (var raw in queries.Where(query => !string.IsNullOrWhiteSpace(query)))
        {
            var forms = sanitize ? TitleNormalizer.QueryVariants(raw) : [CollapseSpaces(raw)];
            for (var index = 0; index < forms.Count; index++)
            {
                var query = CollapseSpaces(forms[index]);
                if (string.IsNullOrWhiteSpace(query) || !seenKeys.Add(EnglishAlternativeTitleFilter.NormalizeForSearchKey(query)))
                {
                    continue;
                }

                (index == 0 ? primary : extra).Add(query);
            }
        }

        primary.AddRange(extra);
        return primary;
    }
}
