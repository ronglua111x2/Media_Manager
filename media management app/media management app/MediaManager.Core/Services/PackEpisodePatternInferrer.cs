using System.Text.RegularExpressions;
using media_management_app.Models;

namespace media_management_app.Services;

public static class PackEpisodePatternInferrer
{
    private static readonly Regex ReleaseRevisionStripRegex = new(
        @"(?:\s*(?:v\d+|proper|repack))(?=\s|$|\.|\[)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CrcHashStripRegex = new(
        @"\s*[\[\(][0-9A-Fa-f]{8}[\]\)]",
        RegexOptions.Compiled);

    private static readonly Regex PrefixSeasonRegex = new(
        @"\bS(?<season>\d{1,2})(?=E|\b|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StandardEpisodeRegex = new(
        @"\bS(?<season>\d{1,3})E(?<episode>\d{1,4})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CompactSeasonEpisodeSuffixRegex = new(
        @"S(?<season>\d{1,2})(?<episode>\d{1,4})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string NormalizeStem(string stem)
    {
        if (string.IsNullOrWhiteSpace(stem))
        {
            return string.Empty;
        }

        var normalized = ReleaseRevisionStripRegex.Replace(stem, string.Empty);
        normalized = CrcHashStripRegex.Replace(normalized, string.Empty);
        return Regex.Replace(normalized, @"\s{2,}", " ").Trim();
    }

    public static int? TryExtractSeasonFromPrefix(string stem, InferredEpisodePattern pattern)
    {
        if (!pattern.IsValid || pattern.PrefixLength <= 0 || string.IsNullOrWhiteSpace(stem))
        {
            return null;
        }

        if (pattern.PrefixLength > stem.Length)
        {
            return null;
        }

        var prefix = stem[..pattern.PrefixLength];
        Match? lastMatch = null;
        foreach (Match match in PrefixSeasonRegex.Matches(prefix))
        {
            lastMatch = match;
        }

        return lastMatch is { Success: true } &&
               int.TryParse(lastMatch.Groups["season"].Value, out var season) &&
               season > 0
            ? season
            : null;
    }

    public static int? TryInferEpisode(
        string stem,
        int seasonNumber,
        IReadOnlySet<int> validEpisodeNumbers,
        InferredEpisodePattern? seasonPattern = null)
    {
        if (string.IsNullOrWhiteSpace(stem) || validEpisodeNumbers.Count == 0)
        {
            return null;
        }

        var normalized = NormalizeStem(stem);

        var compact = TryExtractSeasonEpisodeSuffix(normalized, seasonNumber);
        if (compact is not null && validEpisodeNumbers.Contains(compact.Value))
        {
            return compact;
        }

        if (seasonPattern?.IsValid == true)
        {
            var inferred = TryExtract(normalized, seasonPattern, seasonNumber);
            if (inferred is not null && validEpisodeNumbers.Contains(inferred.Value))
            {
                return inferred;
            }
        }

        var match = StandardEpisodeRegex.Match(normalized);
        if (match.Success &&
            int.TryParse(match.Groups["season"].Value, out var parsedSeason) &&
            int.TryParse(match.Groups["episode"].Value, out var parsedEpisode) &&
            parsedSeason == seasonNumber &&
            validEpisodeNumbers.Contains(parsedEpisode))
        {
            return parsedEpisode;
        }

        return null;
    }

    public static InferredEpisodePattern Infer(
        IReadOnlyList<string> stems,
        int? expectedEpisodeCount = null,
        int? seasonNumber = null,
        IReadOnlyList<InferredEpisodePattern>? borrowCandidates = null)
    {
        if (stems.Count == 0)
        {
            return InferredEpisodePattern.Invalid;
        }

        if (seasonNumber is > 0 && TryBuildCompactSeasonPattern(stems, seasonNumber.Value, out var compactPattern))
        {
            if (expectedEpisodeCount is not > 0 || ValidatePattern(compactPattern, stems, expectedEpisodeCount.Value))
            {
                return compactPattern;
            }
        }

        if (stems.Count == 1)
        {
            return InferSingle(stems[0], seasonNumber, borrowCandidates) ?? InferredEpisodePattern.Invalid;
        }

        var sorted = NaturalSort(stems, seasonNumber);
        var votes = new Dictionary<(int Prefix, int Suffix), int>();

        for (var index = 0; index < sorted.Count - 1; index++)
        {
            var left = sorted[index];
            var right = sorted[index + 1];
            if (!TryGetPatternFromPair(left, right, out var prefixLength, out var suffixLength, out var leftEpisode, out var rightEpisode))
            {
                continue;
            }

            if (rightEpisode - leftEpisode is < 1 or > 3)
            {
                continue;
            }

            var key = (prefixLength, suffixLength);
            votes[key] = votes.GetValueOrDefault(key) + 1;
        }

        if (votes.Count == 0)
        {
            return TryStandardEpisodeBootstrap(sorted, seasonNumber) ?? InferredEpisodePattern.Invalid;
        }

        var best = votes.OrderByDescending(pair => pair.Value).First();
        var pattern = new InferredEpisodePattern
        {
            PrefixLength = best.Key.Prefix,
            SuffixLength = best.Key.Suffix,
            IsValid = true
        };

        if (expectedEpisodeCount is > 0 && !ValidatePattern(pattern, sorted, expectedEpisodeCount.Value))
        {
            if (seasonNumber is > 0 &&
                TryBuildCompactSeasonPattern(stems, seasonNumber.Value, out var fallbackPattern))
            {
                return fallbackPattern;
            }

            return TryStandardEpisodeBootstrap(sorted, seasonNumber) ?? pattern;
        }

        return pattern;
    }

    public static int? TryExtractSeasonEpisodeSuffix(string stem, int seasonNumber)
    {
        if (string.IsNullOrWhiteSpace(stem))
        {
            return null;
        }

        var match = CompactSeasonEpisodeSuffixRegex.Match(stem);
        if (!match.Success ||
            !int.TryParse(match.Groups["season"].Value, out var parsedSeason) ||
            !int.TryParse(match.Groups["episode"].Value, out var parsedEpisode))
        {
            return null;
        }

        return parsedSeason == seasonNumber ? parsedEpisode : null;
    }

    public static IReadOnlyDictionary<string, int?> MapEpisodes(
        IReadOnlyList<string> stems,
        InferredEpisodePattern pattern,
        int? seasonNumber = null)
    {
        var mapped = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        foreach (var stem in stems)
        {
            mapped[stem] = TryExtract(stem, pattern, seasonNumber);
        }

        return mapped;
    }

    public static int? TryExtract(string stem, InferredEpisodePattern pattern, int? seasonNumber = null)
    {
        if (!pattern.IsValid)
        {
            return null;
        }

        if (pattern.CompactSeasonNumber is int compactSeason)
        {
            return TryExtractSeasonEpisodeSuffix(stem, compactSeason);
        }

        var fixedWidth = pattern.TryExtractFixedWidth(stem);
        if (fixedWidth is not null)
        {
            return fixedWidth;
        }

        if (seasonNumber is > 0)
        {
            return TryExtractSeasonEpisodeSuffix(stem, seasonNumber.Value);
        }

        return null;
    }

    private static bool TryBuildCompactSeasonPattern(
        IReadOnlyList<string> stems,
        int seasonNumber,
        out InferredEpisodePattern pattern)
    {
        pattern = InferredEpisodePattern.Invalid;
        var matched = stems.Count(stem => TryExtractSeasonEpisodeSuffix(stem, seasonNumber) is not null);
        if (matched < Math.Max(2, stems.Count / 2))
        {
            return false;
        }

        pattern = InferredEpisodePattern.ForCompactSeason(seasonNumber);
        return true;
    }

    private static bool ValidatePattern(InferredEpisodePattern pattern, IReadOnlyList<string> stems, int expectedEpisodeCount)
    {
        var extracted = stems
            .Select(stem => TryExtract(stem, pattern, pattern.CompactSeasonNumber))
            .Where(episode => episode is not null)
            .Select(episode => episode!.Value)
            .Distinct()
            .ToList();

        if (extracted.Count == 0)
        {
            return false;
        }

        if (extracted.Any(episode => episode < 1 || episode > expectedEpisodeCount + 2))
        {
            return false;
        }

        return extracted.Count >= Math.Min(stems.Count, expectedEpisodeCount) / 2;
    }

    private static InferredEpisodePattern? InferSingle(
        string stem,
        int? seasonNumber,
        IReadOnlyList<InferredEpisodePattern>? borrowCandidates)
    {
        if (seasonNumber is > 0 && TryExtractSeasonEpisodeSuffix(stem, seasonNumber.Value) is not null)
        {
            return InferredEpisodePattern.ForCompactSeason(seasonNumber.Value);
        }

        if (borrowCandidates is not null)
        {
            foreach (var candidate in borrowCandidates.Where(pattern => pattern.IsValid))
            {
                if (TryExtract(stem, candidate, seasonNumber) is not null)
                {
                    return candidate;
                }
            }
        }

        return TryStandardEpisodeBootstrap([stem], seasonNumber);
    }

    private static InferredEpisodePattern? TryStandardEpisodeBootstrap(
        IReadOnlyList<string> stems,
        int? seasonNumber)
    {
        foreach (var stem in stems)
        {
            if (seasonNumber is > 0 && TryExtractSeasonEpisodeSuffix(stem, seasonNumber.Value) is not null)
            {
                return InferredEpisodePattern.ForCompactSeason(seasonNumber.Value);
            }

            var match = StandardEpisodeRegex.Match(stem);
            if (!match.Success)
            {
                continue;
            }

            if (seasonNumber is > 0 &&
                int.TryParse(match.Groups["season"].Value, out var parsedSeason) &&
                parsedSeason != seasonNumber.Value)
            {
                continue;
            }

            var episodeText = match.Groups["episode"].Value;
            var episodeIndex = stem.LastIndexOf(episodeText, StringComparison.OrdinalIgnoreCase);
            if (episodeIndex < 0)
            {
                continue;
            }

            return new InferredEpisodePattern
            {
                PrefixLength = episodeIndex,
                SuffixLength = stem.Length - episodeIndex - episodeText.Length,
                IsValid = true
            };
        }

        return null;
    }

    private static bool TryGetPatternFromPair(
        string left,
        string right,
        out int prefixLength,
        out int suffixLength,
        out int leftEpisode,
        out int rightEpisode)
    {
        prefixLength = 0;
        suffixLength = 0;
        leftEpisode = 0;
        rightEpisode = 0;

        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var maxPrefix = Math.Min(left.Length, right.Length);
        while (prefixLength < maxPrefix && left[prefixLength] == right[prefixLength])
        {
            prefixLength++;
        }

        var maxSuffix = Math.Min(left.Length - prefixLength, right.Length - prefixLength);
        while (suffixLength < maxSuffix &&
               left[left.Length - 1 - suffixLength] == right[right.Length - 1 - suffixLength])
        {
            suffixLength++;
        }

        var leftMiddle = left.Substring(prefixLength, left.Length - prefixLength - suffixLength);
        var rightMiddle = right.Substring(prefixLength, right.Length - prefixLength - suffixLength);
        if (leftMiddle.Length == 0 ||
            rightMiddle.Length == 0 ||
            !leftMiddle.All(char.IsDigit) ||
            !rightMiddle.All(char.IsDigit))
        {
            prefixLength = 0;
            suffixLength = 0;
            return false;
        }

        if (!int.TryParse(leftMiddle, out leftEpisode) || !int.TryParse(rightMiddle, out rightEpisode))
        {
            prefixLength = 0;
            suffixLength = 0;
            return false;
        }

        return true;
    }

    private static List<string> NaturalSort(IReadOnlyList<string> stems, int? seasonNumber)
    {
        return stems
            .OrderBy(stem => ExtractEpisodeSortKey(stem, seasonNumber))
            .ThenBy(stem => stem, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int ExtractEpisodeSortKey(string stem, int? seasonNumber)
    {
        if (seasonNumber is > 0)
        {
            var compact = TryExtractSeasonEpisodeSuffix(stem, seasonNumber.Value);
            if (compact is not null)
            {
                return compact.Value;
            }
        }

        var trailing = Regex.Match(stem, @"(?<episode>\d+)\s*$");
        return trailing.Success && int.TryParse(trailing.Groups["episode"].Value, out var episode)
            ? episode
            : int.MaxValue;
    }
}
