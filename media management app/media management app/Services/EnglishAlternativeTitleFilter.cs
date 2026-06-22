using System.Text;
using System.Text.RegularExpressions;

namespace media_management_app.Services;

/// <summary>
/// Keeps English-market and Japanese romaji alternative titles from TMDB.
/// Rejects non-Latin scripts, unrelated English-market spinoffs, and sequel duplicates.
/// </summary>
public static class EnglishAlternativeTitleFilter
{
    private static readonly HashSet<string> EnglishMarketCountries = new(StringComparer.OrdinalIgnoreCase)
    {
        "US", "GB", "AU", "CA", "NZ", "IE"
    };

    private static readonly HashSet<string> SequelSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "2", "3", "4", "5", "Kan", "Zoku", "Too", "Too!", "Climax"
    };

    private static readonly Regex SignificantTokenRegex = new(@"[A-Za-z]{3,}", RegexOptions.Compiled);

    private static readonly HashSet<string> TokenStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "the", "for", "from", "with", "between", "into", "over", "under", "about",
        "after", "before", "during", "through", "against", "among", "around", "behind",
        "beside", "beyond", "inside", "outside", "under", "until", "upon", "within",
        "without", "while", "where", "when", "what", "which", "who", "whom", "whose",
        "why", "how", "all", "any", "are", "was", "were", "been", "being", "have",
        "has", "had", "does", "did", "will", "would", "could", "should", "may",
        "might", "must", "can", "not", "but", "nor", "yet", "so", "too", "very",
        "just", "only", "also", "than", "then", "them", "they", "their", "there",
        "these", "those", "this", "that", "each", "every", "both", "few", "more",
        "most", "other", "some", "such", "own", "same", "last", "next", "first",
        "new", "old", "long", "short", "high", "low", "big", "small", "good",
        "bad", "best", "worst", "man", "men", "boy", "girl", "day", "way", "may",
        "say", "she", "her", "him", "his", "its", "our", "out", "use"
    };

    private static readonly string[] SpinoffNoisePhrases =
    [
        "fan letter",
        " log",
        "log:",
        " saga",
        "saga:",
        " recap",
        "anthology",
        " spin-off",
        " spinoff",
        " tales from",
        "collection:",
        " shorts",
        " miniseries",
        " mini-series"
    ];

    public sealed record AltTitleEntry(string Title, string? CountryCode, string? Type);

    public static bool IsEnglishMarketCountry(string? countryCode) =>
        !string.IsNullOrWhiteSpace(countryCode) && EnglishMarketCountries.Contains(countryCode.Trim());

    public static bool IsJapaneseMarketCountry(string? countryCode) =>
        string.Equals(countryCode?.Trim(), "JP", StringComparison.OrdinalIgnoreCase);

    public static bool IsPromotionalType(string? type) =>
        string.Equals(type?.Trim(), "promotional title", StringComparison.OrdinalIgnoreCase);

    public static bool IsRomanizationType(string? type) =>
        string.Equals(type?.Trim(), "romanization", StringComparison.OrdinalIgnoreCase);

    public static bool ContainsDisallowedScript(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        foreach (var character in title)
        {
            if (!char.IsLetter(character))
            {
                continue;
            }

            if (character is >= '\u0370' and <= '\u03FF')
            {
                return true;
            }

            if (character is >= '\u0400' and <= '\u04FF')
            {
                return true;
            }

            if (character is >= '\u0590' and <= '\u05FF')
            {
                return true;
            }

            if (character is >= '\u0600' and <= '\u06FF')
            {
                return true;
            }

            if (character is >= '\u3040' and <= '\u30FF')
            {
                return true;
            }

            if (character is >= '\u3000' and <= '\u9FFF')
            {
                return true;
            }

            if (character is >= '\uAC00' and <= '\uD7AF')
            {
                return true;
            }

            if (character is >= '\u10A0' and <= '\u10FF')
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsAllowedLatinTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        if (ContainsDisallowedScript(title))
        {
            return false;
        }

        return title.Any(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
    }

    public static bool HasAnimeAlternativeSignals(IEnumerable<AltTitleEntry> fromApi) =>
        fromApi.Any(entry =>
            IsJapaneseMarketCountry(entry.CountryCode) || IsRomanizationType(entry.Type));

    public static bool ShouldIncludeOriginalTitle(
        string? originalTitle,
        string primaryTitle,
        IEnumerable<AltTitleEntry> fromApi)
    {
        if (string.IsNullOrWhiteSpace(originalTitle))
        {
            return false;
        }

        var normalized = originalTitle.Trim();
        if (string.Equals(normalized, primaryTitle.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IsAllowedLatinTitle(normalized))
        {
            return false;
        }

        return HasAnimeAlternativeSignals(fromApi);
    }

    public static bool ShouldIncludeEntry(AltTitleEntry entry, string primaryTitle)
    {
        if (string.IsNullOrWhiteSpace(entry.Title))
        {
            return false;
        }

        if (IsPromotionalType(entry.Type))
        {
            return false;
        }

        var normalized = entry.Title.Trim();
        if (string.Equals(normalized, primaryTitle.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IsAllowedLatinTitle(normalized))
        {
            return false;
        }

        if (ContainsSpinoffNoise(normalized) || IsDerivativeSpinoffTitle(normalized, primaryTitle))
        {
            return false;
        }

        if (IsRomanizationType(entry.Type) || IsJapaneseMarketCountry(entry.CountryCode))
        {
            return true;
        }

        if (!IsEnglishMarketCountry(entry.CountryCode))
        {
            return false;
        }

        return SharesSignificantTokenWithPrimary(normalized, primaryTitle);
    }

    public static bool ContainsSpinoffNoise(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        var lower = title.ToLowerInvariant();
        return SpinoffNoisePhrases.Any(phrase => lower.Contains(phrase, StringComparison.Ordinal));
    }

    public static bool IsDerivativeSpinoffTitle(string title, string primaryTitle)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(primaryTitle))
        {
            return false;
        }

        var normalized = title.Trim();
        var primary = primaryTitle.Trim();
        if (normalized.Length <= primary.Length)
        {
            return false;
        }

        if (!normalized.StartsWith(primary, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remainder = normalized[primary.Length..].TrimStart(' ', ':', '-', '.', '!');
        return remainder.Length > 0 && ContainsSpinoffNoise(remainder);
    }

    public static bool SharesSignificantTokenWithPrimary(string title, string primaryTitle)
    {
        var primaryTokens = ExtractSignificantTokens(primaryTitle);
        if (primaryTokens.Count == 0)
        {
            return true;
        }

        foreach (var token in ExtractSignificantTokens(title))
        {
            if (primaryTokens.Contains(token))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<string> ExtractSignificantTokens(string title)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in SignificantTokenRegex.Matches(title))
        {
            if (!TokenStopWords.Contains(match.Value))
            {
                tokens.Add(match.Value);
            }
        }

        return tokens;
    }

    public static string NormalizeForSearchKey(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(title.Length);
        foreach (var character in title.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
            else if (char.IsWhiteSpace(character) && builder.Length > 0 && builder[^1] != ' ')
            {
                builder.Append(' ');
            }
        }

        return builder.ToString().Trim();
    }

    public static List<string> CollapseTitlesForSearch(IEnumerable<string> titles)
    {
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var title in titles)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                if (seenKeys.Add(string.Empty))
                {
                    result.Add(string.Empty);
                }

                continue;
            }

            var trimmed = title.Trim();
            var key = NormalizeForSearchKey(trimmed);
            if (string.IsNullOrEmpty(key))
            {
                key = trimmed;
            }

            if (!seenKeys.Add(key))
            {
                continue;
            }

            result.Add(trimmed);
        }

        return result;
    }

    public static bool IsNearDuplicateTitleForSearch(string left, string right, string primaryTitle)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        if (string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var leftKey = NormalizeForSearchKey(left);
        var rightKey = NormalizeForSearchKey(right);
        if (!string.IsNullOrEmpty(leftKey) &&
            string.Equals(leftKey, rightKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var jaccard = ComputeTokenJaccard(left, right);
        if (jaccard >= 0.85)
        {
            return true;
        }

        var sharedTokens = CountSharedSignificantTokens(left, right);
        if (sharedTokens >= 2 && jaccard >= 0.4)
        {
            return true;
        }

        if (sharedTokens >= 1 &&
            left.Trim().Length > 25 &&
            right.Trim().Length > 25 &&
            jaccard >= 0.35)
        {
            return true;
        }

        var primaryKey = NormalizeForSearchKey(primaryTitle);
        if (!string.IsNullOrEmpty(primaryKey) &&
            !string.IsNullOrEmpty(leftKey) &&
            !string.IsNullOrEmpty(rightKey) &&
            leftKey.Length >= primaryKey.Length &&
            rightKey.Length >= primaryKey.Length &&
            leftKey.StartsWith(primaryKey, StringComparison.Ordinal) &&
            rightKey.StartsWith(primaryKey, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    public static int ScoreTitleForSearch(string title, string primaryTitle)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return int.MinValue;
        }

        var trimmed = title.Trim();
        if (string.Equals(NormalizeForSearchKey(trimmed), NormalizeForSearchKey(primaryTitle), StringComparison.OrdinalIgnoreCase))
        {
            return int.MinValue / 2;
        }

        var score = trimmed.Length switch
        {
            <= 10 => 60,
            <= 18 => 45,
            <= 28 => 25,
            <= 40 => 10,
            _ => -20
        };

        var jaccard = ComputeTokenJaccard(trimmed, primaryTitle);
        if (jaccard < 0.25)
        {
            score += 70;
        }
        else if (jaccard < 0.55)
        {
            score += 35;
        }
        else if (jaccard >= 0.85)
        {
            score -= 60;
        }
        else
        {
            score -= 25;
        }

        return score;
    }

    public static List<string> SelectLibraryAlternativeTitlesForSearch(
        IEnumerable<string> libraryAlternativeTitles,
        string primaryTitle,
        int maxCount)
    {
        if (maxCount <= 0)
        {
            return [];
        }

        var candidates = CollapseTitlesForSearch(
            libraryAlternativeTitles
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .Select(title => title.Trim()));

        if (candidates.Count == 0)
        {
            return [];
        }

        var primaryKey = NormalizeForSearchKey(primaryTitle);
        var scored = candidates
            .Where(title => !string.Equals(NormalizeForSearchKey(title), primaryKey, StringComparison.OrdinalIgnoreCase))
            .Select(title => (Title: title, Score: ScoreTitleForSearch(title, primaryTitle)))
            .Where(entry => entry.Score > int.MinValue / 4)
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Title.Length)
            .ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var selected = new List<string>();
        foreach (var candidate in scored)
        {
            if (selected.Any(existing => IsNearDuplicateTitleForSearch(existing, candidate.Title, primaryTitle)))
            {
                continue;
            }

            selected.Add(candidate.Title);
            if (selected.Count >= maxCount)
            {
                break;
            }
        }

        return selected;
    }

    private static double ComputeTokenJaccard(string left, string right)
    {
        var leftTokens = ExtractSignificantTokens(left);
        var rightTokens = ExtractSignificantTokens(right);
        if (leftTokens.Count == 0 && rightTokens.Count == 0)
        {
            return 1d;
        }

        if (leftTokens.Count == 0 || rightTokens.Count == 0)
        {
            return 0d;
        }

        var intersection = leftTokens.Intersect(rightTokens, StringComparer.OrdinalIgnoreCase).Count();
        var union = leftTokens.Union(rightTokens, StringComparer.OrdinalIgnoreCase).Count();
        return union == 0 ? 0d : (double)intersection / union;
    }

    private static int CountSharedSignificantTokens(string left, string right) =>
        ExtractSignificantTokens(left).Intersect(ExtractSignificantTokens(right), StringComparer.OrdinalIgnoreCase).Count();

    private static bool IsSequelSuffix(string suffix)
    {
        if (string.IsNullOrWhiteSpace(suffix))
        {
            return false;
        }

        var trimmed = suffix.Trim();
        if (SequelSuffixes.Contains(trimmed))
        {
            return true;
        }

        return int.TryParse(trimmed, out var number) && number >= 2;
    }

    private static bool IsSequelVariantOf(string candidate, string baseTitle)
    {
        if (candidate.Length <= baseTitle.Length)
        {
            return false;
        }

        if (candidate.StartsWith(baseTitle, StringComparison.OrdinalIgnoreCase))
        {
            var remainder = candidate[baseTitle.Length..].TrimStart(' ', ':', '.', '-');
            return IsSequelSuffix(remainder)
                || (remainder.Length > 0 && char.IsDigit(remainder[0]) && int.TryParse(remainder.Split(' ', ':', '.', '-')[0], out _));
        }

        var normalizedCandidate = NormalizeForSearchKey(candidate);
        var normalizedBase = NormalizeForSearchKey(baseTitle);
        if (!normalizedCandidate.StartsWith(normalizedBase + " ", StringComparison.Ordinal))
        {
            return false;
        }

        var normalizedSuffix = normalizedCandidate[(normalizedBase.Length + 1)..].Trim();
        return IsSequelSuffix(normalizedSuffix);
    }

    private static List<string> CollapseRedundantTitles(IReadOnlyList<string> titles)
    {
        if (titles.Count <= 1)
        {
            return titles.ToList();
        }

        var remove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = titles.OrderBy(title => title.Length).ToList();

        foreach (var shorter in ordered)
        {
            foreach (var longer in ordered)
            {
                if (string.Equals(shorter, longer, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (IsSequelVariantOf(longer, shorter))
                {
                    remove.Add(longer);
                }
            }
        }

        var deduped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var title in titles)
        {
            if (remove.Contains(title))
            {
                continue;
            }

            var key = NormalizeForSearchKey(title);
            if (!deduped.TryGetValue(key, out var existing) || title.Length < existing.Length)
            {
                deduped[key] = title;
            }
        }

        return deduped.Values.ToList();
    }

    public static List<string> BuildList(
        string primaryTitle,
        string? originalTitle,
        IEnumerable<AltTitleEntry> fromApi)
    {
        var apiEntries = fromApi.ToList();
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(primaryTitle))
        {
            excluded.Add(primaryTitle.Trim());
        }

        var titles = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddCandidate(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return;
            }

            var normalized = title.Trim();
            if (excluded.Contains(normalized) || !seen.Add(normalized))
            {
                return;
            }

            if (!IsAllowedLatinTitle(normalized))
            {
                return;
            }

            if (ContainsSpinoffNoise(normalized) || IsDerivativeSpinoffTitle(normalized, primaryTitle))
            {
                return;
            }

            titles.Add(normalized);
        }

        if (ShouldIncludeOriginalTitle(originalTitle, primaryTitle, apiEntries))
        {
            AddCandidate(originalTitle);
        }

        foreach (var entry in apiEntries)
        {
            if (!ShouldIncludeEntry(entry, primaryTitle))
            {
                continue;
            }

            AddCandidate(entry.Title);
        }

        var romanizedTitles = apiEntries
            .Where(entry => IsRomanizationType(entry.Type) || IsJapaneseMarketCountry(entry.CountryCode))
            .Select(entry => entry.Title.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return CollapseRedundantTitles(titles)
            .OrderByDescending(title => romanizedTitles.Contains(title))
            .ThenBy(title => title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
