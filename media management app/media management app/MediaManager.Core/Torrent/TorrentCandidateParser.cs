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

    /// <summary>Last episode of a multi-episode file (S01E01E02, S01E01-E02, S01E01-02); null for one episode.</summary>
    public int? LastEpisodeNumber { get; init; }

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
        @"(?<title>.*?)(?:\bS(?<season>\d{1,3})E(?<episode>\d{1,4})(?:E\d{1,4})*(?:v\d{1,2})?\b|\b(?<season2>\d{1,2})x(?<episode2>\d{1,3})(?:v\d{1,2})?\b)(?<rest>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AbsoluteDigitCandidateRegex = new(
        @"\bEP\s*(?<episode>\d{1,4})(?:v\d{1,2})?\b|\b(?<episode>\d{2,4})(?:v\d{1,2})?\b",
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
        @"\b(?:NCED|NCOP)(?:\d{1,2}[a-z]?)?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // "2026.11.10" / "2026-11-10" after normalization: a daily episode, not "episode 11".
    private static readonly Regex AirDateRegex = new(
        @"\b(?:19|20)\d{2}\s(?:0?[1-9]|1[0-2])\s(?:0?[1-9]|[12]\d|3[01])\b",
        RegexOptions.Compiled);
    // Multi-episode markers on the raw name, before "-" is normalized away: E01E02, E01-E02, E01-02.
    private static readonly Regex MultiEpisodeRegex = new(
        @"\bS(?<season>\d{1,3})E(?<first>\d{1,4})(?:(?:[-_.]?E|-)(?<next>\d{1,4}))+(?:v\d{1,2})?(?![0-9a-z])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LeadingEpisodeContinuationRegex = new(
        @"^(?:\s*E?\d{1,4}\b)+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // "OVA 02", "Special 1", "SP01": a keyword that names a special by number.
    private static readonly Regex SpecialKeywordWithNumberRegex = new(
        @"\b(?:OVA|OAD|Specials?|SP)\s*\d{1,3}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // "Season 1-3", "Season 1 & 2", "S01-S03". A dash or tilde followed by an episode range ("Season 2 - 13-24",
    // "Season 1 - 01~12") names episodes, not seasons (audit L1-L1).
    private static readonly Regex FolderSeasonRangeRegex = new(
        @"\bSeasons?\s*\d{1,2}\s*(?:(?:-|~)\s*(?:Season\s*)?\d{1,2}(?!\s*(?:-|~)\s*\d)|(?:to|&|\+|,|and)\s*(?:Season\s*)?\d{1,2})\b|\bS\d{1,2}\s*(?:-|~|to|&|\+)\s*S?\d{1,2}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FolderSeasonWordRegex = new(
        @"\b(?<word>Seasons?)\s*(?<season>\d{1,2})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FolderSeasonPartRegex = new(
        @"\bSeason\s*(?<season>\d{1,2})\s*[-._,]?\s*(?:Part|Cour)\s*(?<part>\d{1,2})\b",
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
        int? lastEpisode = null;
        int? absoluteEpisode = null;

        if (match.Success)
        {
            showPart = match.Groups["title"].Value;
            rest = match.Groups["rest"].Value;
            var seasonText = match.Groups["season"].Success ? match.Groups["season"].Value : match.Groups["season2"].Value;
            var episodeText = match.Groups["episode"].Success ? match.Groups["episode"].Value : match.Groups["episode2"].Value;
            season = int.TryParse(seasonText, out var seasonValue) ? seasonValue : null;
            episode = int.TryParse(episodeText, out var episodeValue) ? episodeValue : null;
            lastEpisode = TryParseLastEpisode(fileName, season, episode);
            if (lastEpisode is not null)
            {
                rest = LeadingEpisodeContinuationRegex.Replace(rest, string.Empty);
            }
        }
        else if (allowAnimeAbsolute &&
                 !AirDateRegex.IsMatch(normalized) &&
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
        var hasRegularEpisode = season is > 0 && episode is not null;
        var specialSignals = DetectSpecialContent(normalizedPath, fileName, normalized, hasRegularEpisode);
        var isSpecialContent = !isExtraContent && specialSignals.IsSpecialContent;

        return new TorrentCandidateParseResult
        {
            RawTitle = fileName,
            RelativePath = relativePath,
            NormalizedTitle = normalizedTitle,
            ExplicitYear = explicitYear,
            SeasonNumber = season,
            EpisodeNumber = episode,
            LastEpisodeNumber = lastEpisode,
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

    private static int? TryParseLastEpisode(string fileName, int? season, int? episode)
    {
        if (season is null || episode is null)
        {
            return null;
        }

        foreach (Match match in MultiEpisodeRegex.Matches(fileName))
        {
            if (!int.TryParse(match.Groups["season"].Value, out var matchSeason) ||
                !int.TryParse(match.Groups["first"].Value, out var first) ||
                matchSeason != season ||
                first != episode)
            {
                continue;
            }

            var captures = match.Groups["next"].Captures;
            if (captures.Count == 0 || !int.TryParse(captures[^1].Value, out var last))
            {
                continue;
            }

            // A range of a few episodes, never a year or a resolution.
            return last > first && last - first <= 20 ? last : null;
        }

        return null;
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
        string normalizedFileName,
        bool hasRegularEpisode)
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

        // A keyword in the show or episode title ("Special Ops", "A Very Special Episode") does not
        // make an SxxEyy file a special; it counts only without a regular episode number, or when it
        // names the special by number (PARSE-03).
        var keywordSpecial = (ContainsKeyword(normalizedFileName, "ova") || ContainsKeyword(normalizedFileName, "special")) &&
                             (!hasRegularEpisode || SpecialKeywordWithNumberRegex.IsMatch(normalizedFileName));
        if (PathContainsFolder(normalizedPath, "specials") ||
            PathContainsOvaFolder(normalizedPath) ||
            keywordSpecial)
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

    /// <summary>
    /// The season a folder name stands for, only when it names exactly one season ("Season 02",
    /// "Show (Season 2) [BD]", "S02"). Ranges ("Season 1-3", "S01-S03"), "Seasons" and
    /// "Season 1 Part 2" folders give no hint (PARSE-04); Part folders are read by
    /// <see cref="TryGetSeasonPartFromPath"/>.
    /// </summary>
    public static int? TryParseSeasonFolderSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment) || FolderSeasonRangeRegex.IsMatch(segment))
        {
            return null;
        }

        var partMatch = FolderSeasonPartRegex.Match(segment);
        if (partMatch.Success)
        {
            return int.TryParse(partMatch.Groups["part"].Value, out var part) && part <= 1 &&
                   int.TryParse(partMatch.Groups["season"].Value, out var partSeason)
                ? partSeason
                : null;
        }

        var seasonWords = FolderSeasonWordRegex.Matches(segment);
        if (seasonWords.Count > 0)
        {
            if (seasonWords.Any(match => match.Groups["word"].Value.EndsWith("s", StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            // TryParse: .NET \d also matches non-ASCII digits, which int.Parse rejects (audit L1-L2).
            var seasons = seasonWords
                .Select(match => int.TryParse(match.Groups["season"].Value, out var value) ? value : (int?)null)
                .Where(value => value is not null)
                .Select(value => value!.Value)
                .Distinct()
                .ToList();
            return seasons.Count == 1 ? seasons[0] : null;
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

    /// <summary>
    /// "Season 1 Part 2" (or "Cour 2") folder of a file, Part 2 and later only: such folders often
    /// restart episode numbers, so the pack analyzer keeps them apart.
    /// </summary>
    public static (int Season, int Part)? TryGetSeasonPartFromPath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var segments = NormalizePath(relativePath).Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = segments.Length - 2; index >= 0; index--)
        {
            var match = FolderSeasonPartRegex.Match(segments[index]);
            if (match.Success &&
                int.TryParse(match.Groups["season"].Value, out var season) &&
                int.TryParse(match.Groups["part"].Value, out var part) &&
                season > 0 &&
                part > 1)
            {
                return (season, part);
            }
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

        // Release tags after the number ("Show - 10 [1080p][0000000A]") do not hide it; without this,
        // unpadded "- N [CRC]" specials had no index and were ordered by name ("- 10" before "- 2").
        var withoutTrailingTags = Regex.Replace(nameWithoutExtension, @"(?:\s*[\[\(][^\[\]\(\)]*[\]\)])+\s*$", string.Empty);
        var episodeMatch = Regex.Match(
            withoutTrailingTags,
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

    public static IEnumerable<string> Tokenize(string value)
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
