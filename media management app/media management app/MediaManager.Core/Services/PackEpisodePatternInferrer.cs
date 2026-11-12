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

    // "S0105" at the end of a stem. The split between season and episode digits depends on the
    // season asked for ("S1012" is S10E12 for season 10 and S01E012 for season 1), and the "S" must
    // start a word ("ABCS0105" is not a season marker).
    private static readonly Regex CompactSeasonEpisodeSuffixRegex = new(
        @"\bS(?<digits>\d{2,6})\s*$",
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
            return ValidatedOrInvalid(TryStandardEpisodeBootstrap(sorted, seasonNumber), sorted, expectedEpisodeCount);
        }

        // Pairs across a digit-count boundary (09/10) vote for a different prefix than the rest; a
        // longer prefix then reads only the last digit ("- 11" -> 1). Prefer the voted pattern that
        // reads the most distinct numbers without zeros or repeats, then the most votes.
        var pattern = votes
            .Select(pair => new InferredEpisodePattern
            {
                PrefixLength = pair.Key.Prefix,
                SuffixLength = pair.Key.Suffix,
                IsValid = true
            })
            .Select(candidate => (Pattern: candidate, Score: ScoreExtraction(candidate, sorted), Votes: votes[(candidate.PrefixLength, candidate.SuffixLength)]))
            .OrderByDescending(item => item.Score.Clean)
            .ThenByDescending(item => item.Score.Distinct)
            .ThenByDescending(item => item.Votes)
            .ThenBy(item => item.Pattern.PrefixLength)
            .First()
            .Pattern;

        if (expectedEpisodeCount is > 0 && !ValidatePattern(pattern, sorted, expectedEpisodeCount.Value))
        {
            if (seasonNumber is > 0 &&
                TryBuildCompactSeasonPattern(stems, seasonNumber.Value, out var fallbackPattern))
            {
                return fallbackPattern;
            }

            // A pattern that failed validation is a guess; callers skip the files instead (PARSE-01).
            return ValidatedOrInvalid(TryStandardEpisodeBootstrap(sorted, seasonNumber), sorted, expectedEpisodeCount);
        }

        return pattern;
    }

    private static InferredEpisodePattern ValidatedOrInvalid(
        InferredEpisodePattern? pattern,
        IReadOnlyList<string> stems,
        int? expectedEpisodeCount)
    {
        if (pattern is null || !pattern.IsValid)
        {
            return InferredEpisodePattern.Invalid;
        }

        return expectedEpisodeCount is > 0 && !ValidatePattern(pattern, stems, expectedEpisodeCount.Value)
            ? InferredEpisodePattern.Invalid
            : pattern;
    }

    private static (bool Clean, int Distinct) ScoreExtraction(InferredEpisodePattern pattern, IReadOnlyList<string> stems)
    {
        var extracted = stems
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(stem => TryExtract(stem, pattern))
            .Where(episode => episode is not null)
            .Select(episode => episode!.Value)
            .ToList();
        var distinct = extracted.Distinct().Count();
        var clean = extracted.Count > 0 && distinct == extracted.Count && extracted.All(episode => episode > 0);
        return (clean, distinct);
    }

    public static int? TryExtractSeasonEpisodeSuffix(string stem, int seasonNumber)
    {
        if (string.IsNullOrWhiteSpace(stem))
        {
            return null;
        }

        var match = CompactSeasonEpisodeSuffixRegex.Match(stem);
        if (!match.Success || seasonNumber <= 0)
        {
            return null;
        }

        var digits = match.Groups["digits"].Value;
        foreach (var prefix in new[] { seasonNumber.ToString("00"), seasonNumber.ToString() }.Distinct())
        {
            var episodeDigits = digits.Length - prefix.Length;
            if (episodeDigits is >= 1 and <= 4 &&
                digits.StartsWith(prefix, StringComparison.Ordinal) &&
                int.TryParse(digits[prefix.Length..], out var episode) &&
                episode > 0)
            {
                return episode;
            }
        }

        return null;
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
