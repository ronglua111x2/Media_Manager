using System.Text.RegularExpressions;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class ParserService : IParserService
{
    private static readonly Regex EpisodeRegex = new(@"(?<show>.*?)(?:\bS(?<season>\d{1,3})E(?<episode>\d{1,4})\b|\b(?<season2>\d{1,3})x(?<episode2>\d{1,4})\b)\s*(?<rest>.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AnimeAbsoluteRegex = new(@"^\[(?<group>[^\]]+)\]\s*(?<show>.+?)\s*-\s*(?<episode>\d{1,4})(?:\s*(?<rest>.*))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BracketedHashRegex = new(@"\[[0-9a-f]{6,10}\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MovieYearRegex = new(@"^(?<title>.*?)(?:\s|\(|\[)(?<year>(?:19|20)\d{2})(?:\)|\])?(?:\s|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ReleaseTokenRegex = new(@"\b(?:1080p|720p|2160p|480p|bluray|brrip|webrip|web-dl|webdl|hdtv|x264|x265|h264|h265|hevc|aac|dts|hdr|dv|proper|repack|extended|remux|yify|rarbg)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex IgnoredReleaseFileRegex = new(@"\b(?:sample|proof)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
        _logger.Debug($"Parser input. File='{fileName}', Folder='{folderPath}', Candidate='{candidateText}', FolderBase='{folderBase}'", LogTarget.File);

        if (ShouldIgnore(fileBase, candidateText))
        {
            _logger.Info($"Parser ignored likely non-library video file '{fileName}'", LogTarget.File | LogTarget.Console);
            return new ParsedCandidate
            {
                SourceText = candidateText,
                MediaKind = MediaKind.Unknown,
                ParserPattern = ParserPattern.Ignored,
                NeedsReview = false,
                Reason = "Ignored by parser rule"
            };
        }

        _logger.Debug($"Trying standard TV episode parser for '{candidateText}'", LogTarget.File);
        var fileOnly = TryParseText(candidateText);
        if (fileOnly is { NeedsReview: false })
        {
            _logger.Debug($"Standard TV parser matched '{fileName}' as {fileOnly.ShowTitle} S{fileOnly.SeasonNumber:00}E{fileOnly.EpisodeNumber:00}", LogTarget.File);
            return fileOnly;
        }

        var combined = string.IsNullOrWhiteSpace(folderBase) ? candidateText : $"{folderBase} {candidateText}";
        _logger.Debug($"Standard TV parser did not produce confident result. Trying folder-combined text '{combined}'", LogTarget.File);
        var combinedResult = TryParseText(combined);
        if (combinedResult.SeasonNumber is not null && combinedResult.EpisodeNumber is not null)
        {
            _logger.Debug($"Folder-combined TV parser matched '{fileName}' as {combinedResult.ShowTitle} S{combinedResult.SeasonNumber:00}E{combinedResult.EpisodeNumber:00}", LogTarget.File);
            return combinedResult;
        }

        _logger.Debug($"Trying anime absolute episode parser for raw file text '{fileBase}'", LogTarget.File);
        var anime = TryParseAnimeAbsolute(fileBase);
        if (anime.MediaKind == MediaKind.TvEpisode)
        {
            _logger.Debug($"Anime absolute parser matched '{fileName}' as {anime.ShowTitle} S{anime.SeasonNumber:00}E{anime.EpisodeNumber:0000}", LogTarget.File);
            return anime;
        }

        _logger.Debug($"Anime absolute parser did not match. Trying strict movie parser for '{candidateText}'", LogTarget.File);
        var movie = TryParseMovie(candidateText, folderBase, combined);
        if (movie.MediaKind == MediaKind.Movie)
        {
            _logger.Debug($"Strict movie parser matched '{fileName}' as movie {movie.MovieTitle} ({movie.MovieYear})", LogTarget.File);
            return movie;
        }

        _logger.Warning($"Could not confidently classify video item from {fileName}. Marking as Unknown/NeedsReview.", LogTarget.File | LogTarget.Console);
        return new ParsedCandidate
        {
            SourceText = candidateText,
            MediaKind = MediaKind.Unknown,
            ParserPattern = ParserPattern.Unknown,
            NeedsReview = true,
            Reason = "No confident TV, anime, or movie pattern found"
        };
    }

    private static ParsedCandidate TryParseText(string sourceText)
    {
        var match = EpisodeRegex.Match(sourceText);
        if (!match.Success)
        {
            return new ParsedCandidate
            {
                SourceText = sourceText,
                MediaKind = MediaKind.Unknown,
                ParserPattern = ParserPattern.Unknown,
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
            MediaKind = MediaKind.TvEpisode,
            ParserPattern = ParserPattern.StandardTv,
            ShowTitle = showText,
            SeasonNumber = int.TryParse(seasonText, out var season) ? season : null,
            EpisodeNumber = int.TryParse(episodeText, out var episode) ? episode : null,
            EpisodeTitle = string.IsNullOrWhiteSpace(rest) ? null : rest,
            NeedsReview = needsReview,
            Reason = needsReview ? "Show title was uncertain" : null
        };
    }

    private static ParsedCandidate TryParseAnimeAbsolute(string sourceText)
    {
        var match = AnimeAbsoluteRegex.Match(sourceText);
        if (!match.Success)
        {
            return new ParsedCandidate
            {
                SourceText = sourceText,
                MediaKind = MediaKind.Unknown,
                ParserPattern = ParserPattern.Unknown,
                NeedsReview = true,
                Reason = "No anime absolute episode pattern found"
            };
        }

        var showTitle = NormalizeTitle(match.Groups["show"].Value);
        var rest = CleanRemainder(match.Groups["rest"].Value);
        var hasEpisode = int.TryParse(match.Groups["episode"].Value, out var episode);
        var needsReview = string.IsNullOrWhiteSpace(showTitle) || !hasEpisode;

        return new ParsedCandidate
        {
            SourceText = sourceText,
            MediaKind = MediaKind.TvEpisode,
            ParserPattern = ParserPattern.AnimeAbsolute,
            ShowTitle = showTitle,
            SeasonNumber = 1,
            EpisodeNumber = hasEpisode ? episode : null,
            EpisodeTitle = string.IsNullOrWhiteSpace(rest) ? null : rest,
            NeedsReview = needsReview,
            Reason = needsReview ? "Anime absolute episode title or number was uncertain" : null
        };
    }

    private static ParsedCandidate TryParseMovie(string fileText, string folderText, string combinedText)
    {
        var candidates = new[] { fileText, folderText, combinedText }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var withYear = candidates.Select(ParseMovieText).FirstOrDefault(candidate => candidate.MovieYear is not null);
        if (withYear is not null)
        {
            return withYear;
        }

        return new ParsedCandidate
        {
            SourceText = fileText,
            MediaKind = MediaKind.Unknown,
            ParserPattern = ParserPattern.Unknown,
            NeedsReview = true,
            Reason = "Strict movie parser requires a recognizable release year"
        };
    }

    private static ParsedCandidate ParseMovieText(string sourceText)
    {
        var match = MovieYearRegex.Match(sourceText);
        if (!match.Success)
        {
            return new ParsedCandidate
            {
                SourceText = sourceText,
                MediaKind = MediaKind.Movie,
                ParserPattern = ParserPattern.Unknown,
                MovieTitle = CleanMovieTitle(sourceText),
                NeedsReview = true,
                Reason = "Movie year was not found"
            };
        }

        var title = CleanMovieTitle(match.Groups["title"].Value);
        var hasYear = int.TryParse(match.Groups["year"].Value, out var year);
        var needsReview = string.IsNullOrWhiteSpace(title) || !hasYear;
        return new ParsedCandidate
        {
            SourceText = sourceText,
            MediaKind = MediaKind.Movie,
            ParserPattern = ParserPattern.MovieWithYear,
            MovieTitle = title,
            MovieYear = hasYear ? year : null,
            NeedsReview = needsReview,
            Reason = needsReview ? "Movie title or year was uncertain" : null
        };
    }

    private static string NormalizeTitle(string value)
    {
        value = NormalizeText(value);
        value = Regex.Replace(value, @"\b(?:s\d{1,2}e\d{1,2}|\d{1,2}x\d{1,2})\b", string.Empty, RegexOptions.IgnoreCase);
        value = ReleaseTokenRegex.Replace(value, string.Empty);
        return Regex.Replace(value, @"\s{2,}", " ").Trim();
    }

    private static string CleanMovieTitle(string value)
    {
        value = NormalizeText(value);
        value = MovieYearRegex.Replace(value, match => match.Groups["title"].Value);
        value = BracketedHashRegex.Replace(value, string.Empty);
        value = Regex.Replace(value, @"^\[[^\]]+\]\s*", string.Empty);
        value = ReleaseTokenRegex.Replace(value, string.Empty);
        value = Regex.Replace(value, @"\(\s*\)", string.Empty);
        value = Regex.Replace(value, @"\[\s*\]", string.Empty);
        return Regex.Replace(value, @"\s{2,}", " ").Trim();
    }

    private static string CleanRemainder(string value)
    {
        value = NormalizeText(value);
        value = BracketedHashRegex.Replace(value, string.Empty);
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

    private static bool ShouldIgnore(string rawFileBase, string candidateText)
    {
        if (string.Equals(rawFileBase, "sample", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidateText, "sample", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IgnoredReleaseFileRegex.IsMatch(candidateText);
    }
}
