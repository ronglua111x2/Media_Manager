using System.Text.RegularExpressions;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class ParserService : IParserService
{
    private static readonly Regex EpisodeRegex = new(@"(?<show>.*?)(?:\bS(?<season>\d{1,2})E(?<episode>\d{1,2})\b|\b(?<season2>\d{1,2})x(?<episode2>\d{1,2})\b)\s*(?<rest>.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IAppLogger _logger;

    public ParserService(IAppLogger logger)
    {
        _logger = logger;
    }

    public ParsedCandidate Parse(string fileName, string folderPath)
    {
        var fileBase = Path.GetFileNameWithoutExtension(fileName);
        var folderBase = NormalizeText(Path.GetFileName(folderPath));
        var candidateText = NormalizeText(fileBase);
        var fileOnly = TryParseText(candidateText);
        if (fileOnly is { NeedsReview: false })
        {
            _logger.Debug($"Parsed {fileName} as {fileOnly.ShowTitle} S{fileOnly.SeasonNumber:00}E{fileOnly.EpisodeNumber:00}", LogTarget.File);
            return fileOnly;
        }

        var combined = string.IsNullOrWhiteSpace(folderBase) ? candidateText : $"{folderBase} {candidateText}";
        var combinedResult = TryParseText(combined);
        if (combinedResult.SeasonNumber is not null && combinedResult.EpisodeNumber is not null)
        {
            _logger.Debug($"Parsed folder-wrapped item {fileName} as {combinedResult.ShowTitle} S{combinedResult.SeasonNumber:00}E{combinedResult.EpisodeNumber:00}", LogTarget.File);
            return combinedResult;
        }

        _logger.Warning($"Could not parse episode pattern from {fileName}", LogTarget.File | LogTarget.Console);
        return fileOnly;
    }

    private static ParsedCandidate TryParseText(string sourceText)
    {
        var match = EpisodeRegex.Match(sourceText);
        if (!match.Success)
        {
            return new ParsedCandidate
            {
                SourceText = sourceText,
                NeedsReview = true,
                Reason = "No episode pattern found"
            };
        }

        var seasonText = match.Groups["season"].Success ? match.Groups["season"].Value : match.Groups["season2"].Value;
        var episodeText = match.Groups["episode"].Success ? match.Groups["episode"].Value : match.Groups["episode2"].Value;
        var showText = NormalizeTitle(match.Groups["show"].Value);
        var rest = NormalizeText(match.Groups["rest"].Value);
        var needsReview = string.IsNullOrWhiteSpace(showText) || showText.Length < 2;

        return new ParsedCandidate
        {
            SourceText = sourceText,
            ShowTitle = showText,
            SeasonNumber = int.TryParse(seasonText, out var season) ? season : null,
            EpisodeNumber = int.TryParse(episodeText, out var episode) ? episode : null,
            EpisodeTitle = string.IsNullOrWhiteSpace(rest) ? null : rest,
            NeedsReview = needsReview,
            Reason = needsReview ? "Show title was uncertain" : null
        };
    }

    private static string NormalizeTitle(string value)
    {
        value = NormalizeText(value);
        value = Regex.Replace(value, @"\b(?:s\d{1,2}e\d{1,2}|\d{1,2}x\d{1,2})\b", string.Empty, RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\b(?:1080p|720p|2160p|480p|bluray|webrip|webdl|hdtv|x264|x265|aac|proper|repack|hdr)\b", string.Empty, RegexOptions.IgnoreCase);
        return Regex.Replace(value, @"\s{2,}", " ").Trim();
    }

    private static string NormalizeText(string value)
    {
        value = value.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');
        return Regex.Replace(value, @"\s{2,}", " ").Trim();
    }
}
