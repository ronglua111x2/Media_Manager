using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace media_management_app.Services;

/// <summary>
/// One normalization for titles, shared by query building and title matching (native-search P1, SRCH-13).
/// Measured on real engines (docs/code-review/2026-11/07b-hunt-search.md §5c):
/// "Leon The Professional" returned 458 rows vs 207 for "Léon The Professional", and apostrophe
/// handling favours different engines ("Schindlers List" vs "Schindler s List"), so queries get
/// variants while matching folds both sides to the same form.
/// </summary>
public static class TitleNormalizer
{
    private static readonly Regex NonSearchSafeCharacters = new(@"[^\p{L}\p{N}\s-]", RegexOptions.Compiled);
    private static readonly char[] Apostrophes = ['\'', '’', '‘', '`', '´', 'ʼ'];

    private static readonly Dictionary<char, string> ExtraFolds = new()
    {
        ['ø'] = "o", ['Ø'] = "O", ['æ'] = "ae", ['Æ'] = "AE", ['œ'] = "oe", ['Œ'] = "OE",
        ['ß'] = "ss", ['đ'] = "d", ['Đ'] = "D", ['ł'] = "l", ['Ł'] = "L", ['ı'] = "i", ['þ'] = "th", ['Þ'] = "Th"
    };

    // Standalone roman numerals that commonly appear in titles. "I", "V" and "X" are left alone:
    // they are too often words or letters ("I Am Legend", "V for Vendetta", "X-Men").
    private static readonly Dictionary<string, string> RomanNumerals = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ii"] = "2", ["iii"] = "3", ["iv"] = "4", ["vi"] = "6", ["vii"] = "7", ["viii"] = "8", ["ix"] = "9"
    };

    /// <summary>Removes accents/diacritics: "Léon" → "Leon", "Amélie" → "Amelie", "Đà Nẵng" → "Da Nang".</summary>
    public static string FoldDiacritics(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (ExtraFolds.TryGetValue(character, out var replacement))
            {
                builder.Append(replacement);
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    public static bool HasDiacritics(string? value) =>
        !string.IsNullOrEmpty(value) && !string.Equals(FoldDiacritics(value), value, StringComparison.Ordinal);

    public static bool HasApostrophe(string? value) =>
        !string.IsNullOrEmpty(value) && value.IndexOfAny(Apostrophes) >= 0;

    /// <summary>
    /// Primary search form: diacritics folded, apostrophes removed (joined), other punctuation turned
    /// into spaces. "Schindler's List" → "Schindlers List", "Léon: The Professional" → "Leon The Professional".
    /// </summary>
    public static string ToSearchText(string? query) => Sanitize(FoldDiacritics(JoinApostrophes(query)));

    /// <summary>
    /// Search variants for one rendered query, most useful first:
    /// 1. folded + apostrophes joined ("Schindlers List");
    /// 2. folded + apostrophes split ("Schindler s List"), only when the query had an apostrophe;
    /// 3. original letters with accents ("Léon The Professional"), only when the query had diacritics
    ///    (a few engines only match the accented spelling).
    /// </summary>
    public static IReadOnlyList<string> QueryVariants(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var variants = new List<string> { ToSearchText(query) };
        if (HasApostrophe(query))
        {
            variants.Add(Sanitize(FoldDiacritics(query)));
        }

        if (HasDiacritics(query))
        {
            variants.Add(Sanitize(JoinApostrophes(query)));
        }

        return variants
            .Where(variant => !string.IsNullOrWhiteSpace(variant))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Comparable form of a single title token: diacritics folded, lower-case, letters/digits only,
    /// common roman numerals mapped to digits ("II" → "2").
    /// </summary>
    public static string NormalizeToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        var folded = new string(FoldDiacritics(token).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return RomanNumerals.TryGetValue(folded, out var digits) ? digits : folded;
    }

    /// <summary>Tokens of a title or release name in comparable form (see <see cref="NormalizeToken"/>).</summary>
    public static IReadOnlyList<string> MatchTokens(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return TorrentCandidateParser.Tokenize(value)
            .Select(NormalizeToken)
            .Where(token => token.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Title tokens that count for identity matching: normalized, longer than two characters
    /// (same threshold the matchers always used, so "of"/"to"/"a" never decide a match).
    /// </summary>
    public static IReadOnlyList<string> SignificantTitleTokens(string? title, bool distinct = true)
    {
        var tokens = MatchTokens(title).Where(token => token.Length > 2);
        return (distinct ? tokens.Distinct(StringComparer.Ordinal) : tokens).ToList();
    }

    /// <summary>Normalized lookup set for already-tokenized candidate tokens (e.g. parser TitleTokens).</summary>
    public static HashSet<string> NormalizeTokens(IEnumerable<string> tokens) =>
        tokens.Select(NormalizeToken).Where(token => token.Length > 0).ToHashSet(StringComparer.Ordinal);

    public static bool TokenEquals(string? left, string? right) =>
        string.Equals(NormalizeToken(left), NormalizeToken(right), StringComparison.Ordinal);

    private static string JoinApostrophes(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (Array.IndexOf(Apostrophes, character) < 0)
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static string Sanitize(string value) =>
        QueryTemplateRenderer.CollapseSpaces(NonSearchSafeCharacters.Replace(value, " "));
}
