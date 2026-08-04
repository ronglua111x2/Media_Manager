namespace media_management_app.Services;

public static class PreferredTermMatcher
{
    public static int CountMatches(string fileName, string? preferredTerms)
    {
        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(preferredTerms))
        {
            return 0;
        }

        return CountMatches(fileName, SplitTerms(preferredTerms));
    }

    public static int CountMatches(string fileName, IEnumerable<string>? preferredTerms)
    {
        if (string.IsNullOrWhiteSpace(fileName) || preferredTerms is null)
        {
            return 0;
        }

        var terms = preferredTerms
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Select(term => term.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (terms.Count == 0)
        {
            return 0;
        }

        return terms.Count(term => fileName.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<string> SplitTerms(string? preferredTerms)
    {
        if (string.IsNullOrWhiteSpace(preferredTerms))
        {
            return [];
        }

        return preferredTerms
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
