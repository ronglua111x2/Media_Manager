using System.Text.RegularExpressions;

namespace media_management_app.Services;

public sealed class TorrentCandidateParseResult
{
    public string RawTitle { get; init; } = string.Empty;

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
}

public static class TorrentCandidateParser
{
    private static readonly Regex EpisodeRegex = new(
        @"(?<title>.*?)(?:\bS(?<season>\d{1,3})E(?<episode>\d{1,4})\b|\b(?<season2>\d{1,3})x(?<episode2>\d{1,4})\b)(?<rest>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AbsoluteEpisodeRegex = new(
        @"^(?:\[[^\]]+\]\s*)*(?<title>.*?)(?:\bEP\s*(?<episode>\d{1,4})\b|\b(?<episode>\d{2,4})\b)(?<rest>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex YearRegex = new(@"\b(19|20)\d{2}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex YearRangeRegex = new(@"\b(?<from>(?:19|20)\d{2})\s*(?:-|to)\s*(?<to>(?:19|20)\d{2})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SeasonRangeRegex = new(@"\bS(?<from>\d{1,3})\s*(?:-|to)\s*S?(?<to>\d{1,3})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SeasonWordRangeRegex = new(@"\bSeasons?\s*(?<from>\d{1,3})(?:\s*(?:-|to)\s*(?<to>\d{1,3}))?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SeasonSingleRegex = new(@"\bS(?<season>\d{1,3})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ReleaseTokenRegex = new(
        @"\b(?:1080p|720p|2160p|480p|bluray|brrip|webrip|web-dl|webdl|hdtv|x264|x265|h264|h265|hevc|aac|dts|hdr|dv|proper|repack|extended|remux|yify|rarbg|truehd|atmos|ddp|dd\+|ac3|flac|opus)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static TorrentCandidateParseResult Parse(string fileName, bool allowAnimeAbsolute = false)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return new TorrentCandidateParseResult { RawTitle = string.Empty };
        }

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
        else if (allowAnimeAbsolute)
        {
            var absoluteMatch = AbsoluteEpisodeRegex.Match(normalized);
            if (absoluteMatch.Success)
            {
                showPart = absoluteMatch.Groups["title"].Value;
                rest = absoluteMatch.Groups["rest"].Value;
                absoluteEpisode = int.TryParse(absoluteMatch.Groups["episode"].Value, out var absoluteValue)
                    ? absoluteValue
                    : null;
                episode = absoluteEpisode;
            }
        }

        var explicitYear = ExtractYear(showPart);
        var normalizedTitle = NormalizeTitle(showPart);
        var episodeTitle = CleanEpisodeTitle(rest);
        var coveredSeasons = ExtractCoveredSeasons(normalized).ToList();

        return new TorrentCandidateParseResult
        {
            RawTitle = fileName,
            NormalizedTitle = normalizedTitle,
            ExplicitYear = explicitYear,
            SeasonNumber = season,
            EpisodeNumber = episode,
            AbsoluteEpisodeNumber = absoluteEpisode,
            EpisodeTitle = string.IsNullOrWhiteSpace(episodeTitle) ? null : episodeTitle,
            Quality = TorrentQuality.Detect(fileName),
            AudioCodec = DetectAudioCodec(fileName),
            CoveredSeasons = coveredSeasons,
            TitleTokens = Tokenize(normalizedTitle).ToList()
        };
    }

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
