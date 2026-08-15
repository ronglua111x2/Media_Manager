using System.Text.RegularExpressions;

namespace media_management_app.Services;

public sealed class TorrentCandidateParseResult
{
    public string RawTitle { get; init; } = string.Empty;

    public string? RelativePath { get; init; }

    public string NormalizedTitle { get; init; } = string.Empty;

    public int? ExplicitYear { get; init; }

    public int? SeasonNumber { get; init; }

    public int? EpisodeNumber { get; init; }

    public int? AbsoluteEpisodeNumber { get; init; }

    public string? EpisodeTitle { get; init; }

    public string Quality { get; init; } = string.Empty;

    public string AudioCodec { get; init; } = string.Empty;

    public IReadOnlyList<int> CoveredSeasons { get; init; } = [];

    public IReadOnlyList<string> TitleTokens { get; init; } = [];

    public bool IsSpecialContent { get; init; }

    public bool IsExtraContent { get; init; }

    public int? ReleaseSeasonHint { get; init; }

    public int? ReleaseSpecialIndex { get; init; }

    public bool PreferEpisodeIndexMatch { get; init; }
}

public static class TorrentCandidateParser
{
    private static readonly Regex EpisodeRegex = new(
        @"(?<title>.*?)(?:\bS(?<season>\d{1,3})E(?<episode>\d{1,4})\b|\b(?<season2>\d{1,3})x(?<episode2>\d{1,4})\b)(?<rest>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AbsoluteDigitCandidateRegex = new(
        @"\bEP\s*(?<episode>\d{1,4})\b|\b(?<episode>\d{2,4})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex YearRegex = new(@"\b(19|20)\d{2}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex YearRangeRegex = new(@"\b(?<from>(?:19|20)\d{2})\s*(?:-|to)\s*(?<to>(?:19|20)\d{2})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SeasonRangeRegex = new(@"\bS(?<from>\d{1,3})\s*(?:-|to)\s*S?(?<to>\d{1,3})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SeasonWordRangeRegex = new(@"\bSeasons?\s*(?<from>\d{1,3})(?:\s*(?:-|to)\s*(?<to>\d{1,3}))?(?!\s*\+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SeasonPlusListRegex = new(
        @"\bSeasons?\s+(?<list>\d{1,3}(?:\s*\+\s*\d{1,3})+(?:\s*\+\s*(?:OVA|OVAs?|Specials?|Movies?))?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SeasonSingleRegex = new(@"\bS(?<season>\d{1,3})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ReleaseTokenRegex = new(
        @"\b(?:2160p|1440p|1080p|720p|480p|2160|1440|1080|720|480|4k|uhd|ultra\s*hd|ultrahd|qhd|full\s*hd|fullhd|fhd|bluray|brrip|webrip|web-dl|webdl|hdtv|x264|x265|h264|h265|hevc|aac|dts|hdr|dv|proper|repack|extended|remux|yify|rarbg|truehd|atmos|ddp|dd\+|ac3|flac|opus)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OvaBeforeEpisodeRegex = new(
        @"\bOVA\s+S(?<season>\d{1,2})E(?<episode>\d{1,4})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SeasonOvaRegex = new(
        @"\bS(?<season>\d{1,2})OVA\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SeasonSpecialRegex = new(
        @"\bS(?<season>\d{1,2})S(?<special>\d{1,2})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OvaFolderRegex = new(
        @"\bOVA\s*[-._]?\s*(?<index>\d{1,2})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OvaIndexRegex = new(
        @"\bOVA\s*[-._]?\s*(?<index>\d{1,2})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ExtraContentRegex = new(
        @"\b(?:NCED|NCOP)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static TorrentCandidateParseResult Parse(string fileName, bool allowAnimeAbsolute = false) =>
        Parse(fileName, relativePath: null, allowAnimeAbsolute);

    public static TorrentCandidateParseResult Parse(string fileName, string? relativePath, bool allowAnimeAbsolute = false)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return new TorrentCandidateParseResult { RawTitle = string.Empty };
        }

        // Season ranges use hyphens (e.g. "Seasons 1-2", "S01-S02"); extract before NormalizeText strips them.
        var coveredSeasons = ExtractCoveredSeasons(fileName).ToList();
        var normalized = NormalizeText(fileName);
        var match = EpisodeRegex.Match(normalized);
        var showPart = normalized;
        var rest = string.Empty;
        int? season = null;
        int? episode = null;
        int? absoluteEpisode = null;

        if (match.Success)
        {
            showPart = match.Groups["title"].Value;
            rest = match.Groups["rest"].Value;
            var seasonText = match.Groups["season"].Success ? match.Groups["season"].Value : match.Groups["season2"].Value;
            var episodeText = match.Groups["episode"].Success ? match.Groups["episode"].Value : match.Groups["episode2"].Value;
            season = int.TryParse(seasonText, out var seasonValue) ? seasonValue : null;
            episode = int.TryParse(episodeText, out var episodeValue) ? episodeValue : null;
        }
        else if (allowAnimeAbsolute &&
                 !TorrentReleaseKind.HasPackSignalsFromTitle(fileName) &&
                 TryParseAbsoluteEpisode(normalized, out var absoluteShowPart, out var absoluteRest, out var absoluteValue))
        {
            showPart = absoluteShowPart;
            rest = absoluteRest;
            absoluteEpisode = absoluteValue;
            episode = absoluteEpisode;
        }

        var explicitYear = ExtractYear(showPart);
        var normalizedTitle = NormalizeTitle(showPart);
        var episodeTitle = CleanEpisodeTitle(rest);
        var normalizedPath = NormalizePath(relativePath ?? fileName);
        var isExtraContent = DetectExtraContent(normalizedPath, fileName);
        var specialSignals = DetectSpecialContent(normalizedPath, fileName, normalized);
        var isSpecialContent = !isExtraContent && specialSignals.IsSpecialContent;

        return new TorrentCandidateParseResult
        {
            RawTitle = fileName,
            RelativePath = relativePath,
            NormalizedTitle = normalizedTitle,
            ExplicitYear = explicitYear,
            SeasonNumber = season,
            EpisodeNumber = episode,
            AbsoluteEpisodeNumber = absoluteEpisode,
            EpisodeTitle = string.IsNullOrWhiteSpace(episodeTitle) ? null : episodeTitle,
            Quality = TorrentQuality.Detect(fileName),
            AudioCodec = DetectAudioCodec(fileName),
            CoveredSeasons = coveredSeasons,
            TitleTokens = Tokenize(normalizedTitle).ToList(),
            IsSpecialContent = isSpecialContent,
            IsExtraContent = isExtraContent,
            ReleaseSeasonHint = specialSignals.ReleaseSeasonHint,
            ReleaseSpecialIndex = specialSignals.ReleaseSpecialIndex,
            PreferEpisodeIndexMatch = specialSignals.PreferEpisodeIndexMatch
        };
    }

    private static bool TryParseAbsoluteEpisode(
        string normalized,
        out string showPart,
        out string rest,
        out int absoluteEpisode)
    {
        showPart = normalized;
        rest = string.Empty;
        absoluteEpisode = 0;

        // Prefer EP N when present.
        foreach (Match candidate in AbsoluteDigitCandidateRegex.Matches(normalized))
        {
            var episodeGroup = candidate.Groups["episode"];
            if (!episodeGroup.Success ||
                TorrentReleaseKind.IsExcludedAbsoluteEpisodeDigitMatch(normalized, episodeGroup))
            {
                continue;
            }

            if (!int.TryParse(episodeGroup.Value, out absoluteEpisode))
            {
                continue;
            }

            showPart = normalized[..candidate.Index].Trim();
            // Strip leading group tags already handled by leaving showPart as-is after index;
            // drop trailing release junk into rest.
            rest = normalized[(candidate.Index + candidate.Length)..].Trim();
            // If match was via AbsoluteEpisodeRegex-style title (optional leading [group]), keep showPart clean.
            showPart = Regex.Replace(showPart, @"^(?:\[[^\]]+\]\s*)+", string.Empty).Trim();
            return true;
        }

        return false;
    }

    private static bool DetectExtraContent(string normalizedPath, string fileName)
    {
        return PathContainsFolder(normalizedPath, "extras") ||
               ExtraContentRegex.IsMatch(fileName);
    }

    private static (bool IsSpecialContent, int? ReleaseSeasonHint, int? ReleaseSpecialIndex, bool PreferEpisodeIndexMatch) DetectSpecialContent(
        string normalizedPath,
        string fileName,
        string normalizedFileName)
    {
        if (PathContainsFolder(normalizedPath, "extras"))
        {
            return (false, null, null, false);
        }

        var ovaBeforeEpisode = OvaBeforeEpisodeRegex.Match(normalizedFileName);
        if (ovaBeforeEpisode.Success)
        {
            var season = int.TryParse(ovaBeforeEpisode.Groups["season"].Value, out var seasonValue) ? seasonValue : (int?)null;
            var episode = int.TryParse(ovaBeforeEpisode.Groups["episode"].Value, out var episodeValue) ? episodeValue : (int?)null;
            return (true, season, episode, true);
        }

        var seasonOva = SeasonOvaRegex.Match(normalizedFileName);
        if (seasonOva.Success)
        {
            var season = int.TryParse(seasonOva.Groups["season"].Value, out var seasonValue) ? seasonValue : (int?)null;
            return (true, season, null, false);
        }

        var seasonSpecial = SeasonSpecialRegex.Match(normalizedFileName);
        if (seasonSpecial.Success)
        {
            var season = int.TryParse(seasonSpecial.Groups["season"].Value, out var seasonValue) ? seasonValue : (int?)null;
            var special = int.TryParse(seasonSpecial.Groups["special"].Value, out var specialValue) ? specialValue : (int?)null;
            return (true, season, special, false);
        }

        if (PathContainsFolder(normalizedPath, "specials") ||
            PathContainsOvaFolder(normalizedPath) ||
            ContainsKeyword(normalizedFileName, "ova") ||
            ContainsKeyword(normalizedFileName, "special"))
        {
            var fileIndex = ExtractOvaIndexFromText(normalizedFileName);
            var folderIndex = ExtractOvaFolderIndex(normalizedPath);
            var index = fileIndex ?? folderIndex;
            return (true, ExtractSeasonFolderHint(normalizedPath), index, index is not null);
        }

        return (false, null, null, false);
    }

    private static int? ExtractOvaIndexFromText(string normalizedText)
    {
        var match = OvaIndexRegex.Match(normalizedText);
        return match.Success && int.TryParse(match.Groups["index"].Value, out var index)
            ? index
            : null;
    }

    private static int? ExtractOvaFolderIndex(string normalizedPath)
    {
        foreach (var segment in normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var index = ExtractOvaIndexFromText(segment);
            if (index is not null)
            {
                return index;
            }
        }

        return null;
    }

    public static int? TryParseSeasonFolderSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return null;
        }

        var seasonPartMatch = Regex.Match(
            segment,
            @"\bSeason\s*(?<season>\d{1,2})(?:\s*Part\s*\d+)?\b",
            RegexOptions.IgnoreCase);
        if (seasonPartMatch.Success &&
            int.TryParse(seasonPartMatch.Groups["season"].Value, out var seasonFromPart))
        {
            return seasonFromPart;
        }

        var shortMatch = Regex.Match(segment, @"^S(?<season>\d{1,2})$", RegexOptions.IgnoreCase);
        if (shortMatch.Success && int.TryParse(shortMatch.Groups["season"].Value, out var shortSeason))
        {
            return shortSeason;
        }

        var suffixMatch = Regex.Match(segment, @"(?:^|[\s\-])S(?<season>\d{1,2})\s*$", RegexOptions.IgnoreCase);
        if (suffixMatch.Success && int.TryParse(suffixMatch.Groups["season"].Value, out var suffixSeason))
        {
            return suffixSeason;
        }

        return null;
    }

    public static int? TryGetSeasonHintFromPath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var normalizedPath = NormalizePath(relativePath);
        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = segments.Length - 2; index >= 0; index--)
        {
            var season = TryParseSeasonFolderSegment(segments[index]);
            if (season is > 0)
            {
                return season;
            }
        }

        return null;
    }

    public static int? TryParseBareEpisodeIndex(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(nameWithoutExtension))
        {
            return null;
        }

        var episodeMatch = Regex.Match(
            nameWithoutExtension,
            @"(?:^|\s)-\s*(?<episode>\d{1,4})\s*$",
            RegexOptions.IgnoreCase);
        if (episodeMatch.Success && int.TryParse(episodeMatch.Groups["episode"].Value, out var dashEpisode))
        {
            return dashEpisode;
        }

        var explicitMatch = Regex.Match(
            nameWithoutExtension,
            @"\b(?:EP|Episode)\s*(?<episode>\d{1,4})\b",
            RegexOptions.IgnoreCase);
        if (explicitMatch.Success && int.TryParse(explicitMatch.Groups["episode"].Value, out var explicitEpisode))
        {
            return explicitEpisode;
        }

        var eMatch = Regex.Match(nameWithoutExtension, @"\bE(?<episode>\d{1,4})\b", RegexOptions.IgnoreCase);
        if (eMatch.Success && int.TryParse(eMatch.Groups["episode"].Value, out var eEpisode))
        {
            return eEpisode;
        }

        return null;
    }

    private static int? ExtractSeasonFolderHint(string normalizedPath) =>
        TryGetSeasonHintFromPath(normalizedPath);

    private static bool PathContainsFolder(string normalizedPath, string folderName)
    {
        return normalizedPath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => string.Equals(segment, folderName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool PathContainsOvaFolder(string normalizedPath)
    {
        return normalizedPath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment.StartsWith("OVA", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsKeyword(string value, string keyword) =>
        Regex.IsMatch(value, $@"\b{Regex.Escape(keyword)}s?\b", RegexOptions.IgnoreCase);

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    internal static IEnumerable<string> Tokenize(string value)
    {
        return value
            .Replace('.', ' ')
            .Replace('-', ' ')
            .Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => new string(token.Where(char.IsLetterOrDigit).ToArray()))
            .Where(token => !string.IsNullOrWhiteSpace(token));
    }

    private static int? ExtractYear(string value)
    {
        var yearRangeMatch = YearRangeRegex.Match(value);
        if (yearRangeMatch.Success)
        {
            return null;
        }

        var match = YearRegex.Match(value);
        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(match.Value, out var year) ? year : null;
    }

    public static bool ContainsYearRangeIncluding(string value, int year)
    {
        var match = YearRangeRegex.Match(value);
        return match.Success &&
               int.TryParse(match.Groups["from"].Value, out var from) &&
               int.TryParse(match.Groups["to"].Value, out var to) &&
               year >= Math.Min(from, to) &&
               year <= Math.Max(from, to);
    }

    private static IEnumerable<int> ExtractCoveredSeasons(string value)
    {
        var seasons = new SortedSet<int>();
        foreach (Match match in SeasonRangeRegex.Matches(value))
        {
            AddSeasonRange(seasons, match.Groups["from"].Value, match.Groups["to"].Value);
        }

        foreach (Match match in SeasonPlusListRegex.Matches(value))
        {
            AddSeasonsFromPlusList(seasons, match.Groups["list"].Value);
        }

        foreach (Match match in SeasonWordRangeRegex.Matches(value))
        {
            if (match.Groups["to"].Success)
            {
                AddSeasonRange(seasons, match.Groups["from"].Value, match.Groups["to"].Value);
            }
            else if (int.TryParse(match.Groups["from"].Value, out var season))
            {
                seasons.Add(season);
            }
        }

        foreach (Match match in SeasonSingleRegex.Matches(value))
        {
            if (int.TryParse(match.Groups["season"].Value, out var season))
            {
                seasons.Add(season);
            }
        }

        return seasons;
    }

    private static void AddSeasonsFromPlusList(SortedSet<int> seasons, string list)
    {
        foreach (Match digit in Regex.Matches(list, @"\d{1,3}"))
        {
            if (int.TryParse(digit.Value, out var season) && season > 0)
            {
                seasons.Add(season);
            }
        }
    }

    private static void AddSeasonRange(SortedSet<int> seasons, string fromText, string toText)
    {
        if (!int.TryParse(fromText, out var from) || !int.TryParse(toText, out var to))
        {
            return;
        }

        var start = Math.Min(from, to);
        var end = Math.Max(from, to);
        for (var season = start; season <= end; season++)
        {
            seasons.Add(season);
        }
    }

    private static string NormalizeTitle(string value)
    {
        value = NormalizeText(value);
        value = YearRegex.Replace(value, string.Empty);
        value = ReleaseTokenRegex.Replace(value, string.Empty);
        return Regex.Replace(value, @"\s{2,}", " ").Trim();
    }

    private static string CleanEpisodeTitle(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        value = NormalizeText(value);
        value = ReleaseTokenRegex.Replace(value, string.Empty);
        value = Regex.Replace(value, @"\(\s*\)", string.Empty);
        value = Regex.Replace(value, @"\[\s*\]", string.Empty);
        return Regex.Replace(value, @"\s{2,}", " ").Trim();
    }

    private static string NormalizeText(string value)
    {
        value = value.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');
        return Regex.Replace(value, @"\s{2,}", " ").Trim();
    }

    private static string DetectAudioCodec(string fileName)
    {
        string[] codecs = ["TrueHD", "Atmos", "DTS-HD", "DTS", "DDP", "DD+", "AAC", "AC3", "FLAC", "Opus"];
        return codecs.FirstOrDefault(codec => fileName.Contains(codec, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
    }
}
