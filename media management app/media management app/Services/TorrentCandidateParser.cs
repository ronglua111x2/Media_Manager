using System.Text.RegularExpressions;

namespace media_management_app.Services;

public sealed class TorrentCandidateParseResult
{
    public string RawTitle { get; init; } = string.Empty;

    public string NormalizedTitle { get; init; } = string.Empty;

    public int? ExplicitYear { get; init; }

    public int? SeasonNumber { get; init; }

    public int? EpisodeNumber { get; init; }

    public string? EpisodeTitle { get; init; }

    public string Quality { get; init; } = string.Empty;

    public string AudioCodec { get; init; } = string.Empty;

    public IReadOnlyList<string> TitleTokens { get; init; } = [];
}

public static class TorrentCandidateParser
{
    private static readonly Regex EpisodeRegex = new(
        @"(?<title>.*?)(?:\bS(?<season>\d{1,3})E(?<episode>\d{1,4})\b|\b(?<season2>\d{1,3})x(?<episode2>\d{1,4})\b)(?<rest>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex YearRegex = new(@"\b(19|20)\d{2}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ReleaseTokenRegex = new(
        @"\b(?:1080p|720p|2160p|480p|bluray|brrip|webrip|web-dl|webdl|hdtv|x264|x265|h264|h265|hevc|aac|dts|hdr|dv|proper|repack|extended|remux|yify|rarbg|truehd|atmos|ddp|dd\+|ac3|flac|opus)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static TorrentCandidateParseResult Parse(string fileName)
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

        if (match.Success)
        {
            showPart = match.Groups["title"].Value;
            rest = match.Groups["rest"].Value;
            var seasonText = match.Groups["season"].Success ? match.Groups["season"].Value : match.Groups["season2"].Value;
            var episodeText = match.Groups["episode"].Success ? match.Groups["episode"].Value : match.Groups["episode2"].Value;
            season = int.TryParse(seasonText, out var seasonValue) ? seasonValue : null;
            episode = int.TryParse(episodeText, out var episodeValue) ? episodeValue : null;
        }

        var explicitYear = ExtractYear(showPart);
        var normalizedTitle = NormalizeTitle(showPart);
        var episodeTitle = CleanEpisodeTitle(rest);

        return new TorrentCandidateParseResult
        {
            RawTitle = fileName,
            NormalizedTitle = normalizedTitle,
            ExplicitYear = explicitYear,
            SeasonNumber = season,
            EpisodeNumber = episode,
            EpisodeTitle = string.IsNullOrWhiteSpace(episodeTitle) ? null : episodeTitle,
            Quality = DetectQuality(fileName),
            AudioCodec = DetectAudioCodec(fileName),
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
        var match = YearRegex.Match(value);
        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(match.Value, out var year) ? year : null;
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

    private static string DetectQuality(string fileName)
    {
        string[] qualities = ["2160p", "1080p", "720p", "480p"];
        return qualities.FirstOrDefault(quality => fileName.Contains(quality, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
    }

    private static string DetectAudioCodec(string fileName)
    {
        string[] codecs = ["TrueHD", "Atmos", "DTS-HD", "DTS", "DDP", "DD+", "AAC", "AC3", "FLAC", "Opus"];
        return codecs.FirstOrDefault(codec => fileName.Contains(codec, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
    }
}
