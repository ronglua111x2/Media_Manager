using MediaManager.Sonarr.Parser;
using MediaManager.Sonarr.Parser.Model;
using media_management_app.Models;

namespace media_management_app.Services;

public static class PackEpisodeResolver
{
    public static IReadOnlyDictionary<string, PackEpisodeResolution> ResolveSeason(
        IReadOnlyList<(string RelativePath, string FileName)> files,
        int seasonNumber,
        IReadOnlySet<int> validEpisodeNumbers,
        InferredEpisodePattern? fallbackPattern = null,
        IReadOnlyDictionary<string, long>? fileSizes = null,
        AbsoluteEpisodeMapper? absoluteMapper = null,
        IReadOnlyDictionary<DateOnly, IReadOnlyList<(int Season, int Episode)>>? episodesByAirDate = null,
        IReadOnlySet<string>? seasonFolderPaths = null)
    {
        var candidates = new List<PackEpisodeCandidate>();
        var unresolved = new List<(string RelativePath, string Reason)>();
        var shiftedInFolder = new List<PackEpisodeCandidate>();
        var folderNumberingMismatch = false;
        var parsedFiles = files
            .Where(file => !PackSeasonFileGrouper.IsExcludedFromEpisodeGroup(file.RelativePath, file.FileName))
            .Select(file => (file.RelativePath, file.FileName, Parsed: ParsePackFile(file.RelativePath, file.FileName)))
            .ToList();
        var scheme = DecideAbsoluteScheme(
            parsedFiles.Select(file => file.Parsed).ToList(),
            seasonNumber,
            validEpisodeNumbers,
            absoluteMapper);

        foreach (var (relativePath, fileName, parsed) in parsedFiles)
        {
            var stem = Path.GetFileNameWithoutExtension(fileName);
            if (scheme is not null && IsAbsoluteOnly(parsed) && !parsed!.IsDaily)
            {
                var placed = PlaceByScheme(scheme, parsed.AbsoluteEpisodeNumbers[0], seasonNumber, validEpisodeNumbers, out var schemeFailure);
                if (placed is { } episode)
                {
                    candidates.Add(new PackEpisodeCandidate(relativePath, fileName, seasonNumber, episode));
                }
                else
                {
                    unresolved.Add((relativePath, schemeFailure!));
                }

                continue;
            }

            var resolved = TryResolve(parsed, seasonNumber, validEpisodeNumbers, stem, fallbackPattern, absoluteMapper, episodesByAirDate, out var failure);
            var absoluteInSeasonFolder = absoluteMapper is not null &&
                                         IsAbsoluteOnly(parsed) &&
                                         seasonFolderPaths?.Contains(relativePath) == true;
            if (resolved is { } target)
            {
                var candidate = new PackEpisodeCandidate(relativePath, fileName, target.Season, target.Episode, target.LastEpisode);
                if (absoluteInSeasonFolder && !(target.Season == seasonNumber && target.Episode == parsed!.AbsoluteEpisodeNumbers[0]))
                {
                    // Placed by TMDB episode counts, not by its own number: only safe if the whole folder agrees.
                    shiftedInFolder.Add(candidate);
                    folderNumberingMismatch |= target.Season != seasonNumber;
                    continue;
                }

                candidates.Add(candidate);
                continue;
            }

            folderNumberingMismatch |= absoluteInSeasonFolder;
            unresolved.Add((relativePath, failure ?? GetSkipReason(parsed, seasonNumber, validEpisodeNumbers) ?? "No episode pattern matched TMDB season set."));
        }

        // A "Season N" folder whose absolute numbers land partly outside season N (or past the show's
        // last TMDB episode) counts its episodes differently from TMDB, for example a recap TMDB counts
        // and the release does not. Every count-based placement in it is then one episode off, so none
        // is linked (audit L1-H2).
        if (folderNumberingMismatch)
        {
            unresolved.AddRange(shiftedInFolder.Select(candidate => (
                candidate.RelativePath,
                $"Absolute episode numbers in the 'Season {seasonNumber}' folder do not line up with TMDB's episode counts; not linked, review which episodes they are.")));
        }
        else
        {
            candidates.AddRange(shiftedInFolder);
        }

        var resolutions = new Dictionary<string, PackEpisodeResolution>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, resolution) in ResolveCollisions(candidates, fileSizes))
        {
            resolutions[path] = resolution;
        }

        foreach (var (relativePath, reason) in unresolved)
        {
            resolutions[relativePath] = PackEpisodeResolution.Skipped(seasonNumber, reason);
        }

        return resolutions;
    }

    private enum AbsoluteSchemeKind
    {
        SeasonRelative,
        ContinuedFromEarlierSeasons,
        Unclear
    }

    private sealed record AbsoluteScheme(AbsoluteSchemeKind Kind, int Offset, int Lowest, int Highest);

    /// <summary>
    /// One numbering scheme for all absolute-numbered files of a season after the first, decided from the group's
    /// lowest and highest number (audit R2-B-01). Deciding per file mixed a file's own number with numbers counted
    /// through earlier seasons in one season: "- 13" kept as S02E13 next to "- 25" counted to S02E13.
    /// <list type="bullet">
    /// <item>Lowest = episodes of earlier seasons + 1 and highest above this season's count: numbering continues
    /// from earlier seasons, every file is counted.</item>
    /// <item>Lowest inside this season otherwise: season-relative, every file keeps its own number.</item>
    /// <item>Anything else (or a range that fits both readings): unclear, review.</item>
    /// </list>
    /// Season 1 needs no decision: there both readings give the same episode.
    /// </summary>
    private static AbsoluteScheme? DecideAbsoluteScheme(
        IReadOnlyList<ParsedEpisodeInfo?> parsedFiles,
        int seasonNumber,
        IReadOnlySet<int> validEpisodeNumbers,
        AbsoluteEpisodeMapper? absoluteMapper)
    {
        if (absoluteMapper is null || seasonNumber <= 1 || validEpisodeNumbers.Count == 0)
        {
            return null;
        }

        var numbers = parsedFiles
            .Where(parsed => IsAbsoluteOnly(parsed) && !parsed!.IsDaily && parsed.AbsoluteEpisodeNumbers[0] > 0)
            .Select(parsed => parsed!.AbsoluteEpisodeNumbers[0])
            .ToList();
        var offset = absoluteMapper.EpisodesBefore(seasonNumber);
        if (numbers.Count == 0 || offset <= 0)
        {
            return null;
        }

        var lowest = numbers.Min();
        var highest = numbers.Max();
        var seasonCount = validEpisodeNumbers.Max();
        var kind = lowest == offset + 1
            ? highest > seasonCount ? AbsoluteSchemeKind.ContinuedFromEarlierSeasons : AbsoluteSchemeKind.Unclear
            : lowest <= seasonCount
                ? AbsoluteSchemeKind.SeasonRelative
                : AbsoluteSchemeKind.Unclear;
        return new AbsoluteScheme(kind, offset, lowest, highest);
    }

    private static int? PlaceByScheme(
        AbsoluteScheme scheme,
        int absoluteEpisode,
        int seasonNumber,
        IReadOnlySet<int> validEpisodeNumbers,
        out string? failure)
    {
        failure = null;
        switch (scheme.Kind)
        {
            case AbsoluteSchemeKind.ContinuedFromEarlierSeasons:
                var counted = absoluteEpisode - scheme.Offset;
                if (validEpisodeNumbers.Contains(counted))
                {
                    return counted;
                }

                failure = $"Absolute episode {absoluteEpisode} counts past season {seasonNumber}'s TMDB episodes (numbering continues from earlier seasons); not linked.";
                return null;

            case AbsoluteSchemeKind.SeasonRelative:
                if (validEpisodeNumbers.Contains(absoluteEpisode))
                {
                    return absoluteEpisode;
                }

                failure = $"Episode {absoluteEpisode} is above season {seasonNumber}'s TMDB episodes in a season numbered from 1; not linked, review (a bonus or a special?).";
                return null;

            default:
                failure = $"Absolute episode numbers {scheme.Lowest}-{scheme.Highest} in season {seasonNumber} do not line up with TMDB's episode counts " +
                          $"(neither numbered from 1 nor continued after the {scheme.Offset} earlier episodes); not linked, review which episodes they are.";
                return null;
        }
    }

    /// <summary>
    /// Several files that resolve to one episode: a copy with a higher revision or better quality
    /// wins. Copies that do not differ in either are a sign the mapping is wrong (for example a
    /// pattern that read only the last digit), so none of them is linked.
    /// </summary>
    public static IReadOnlyDictionary<string, PackEpisodeResolution> ResolveCollisions(
        IEnumerable<PackEpisodeCandidate> candidates,
        IReadOnlyDictionary<string, long>? fileSizes = null)
    {
        var resolutions = new Dictionary<string, PackEpisodeResolution>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in candidates.GroupBy(item => (item.Season, item.Episode)))
        {
            var ordered = TorrentFileRules.OrderByPreference(
                    group,
                    item => item.RelativePath,
                    item => GetFileSize(fileSizes, item.RelativePath))
                .ToList();
            var (season, episode) = group.Key;
            var winner = ordered[0];

            if (ordered.Count > 1 && !IsBetterCopy(winner, ordered[1]))
            {
                var names = string.Join(", ", ordered.Select(item => $"'{item.FileName}'"));
                foreach (var item in ordered)
                {
                    resolutions[item.RelativePath] = PackEpisodeResolution.Skipped(
                        season,
                        $"{ordered.Count} files resolve to S{season:00}E{episode:00} with the same revision and quality ({names}); not linking a guess.");
                }

                continue;
            }

            resolutions[winner.RelativePath] = PackEpisodeResolution.Linked(season, episode, winner.LastEpisode);
            foreach (var loser in ordered.Skip(1))
            {
                resolutions[loser.RelativePath] = PackEpisodeResolution.Skipped(
                    season,
                    $"Duplicate of S{season:00}E{episode:00}; kept '{winner.FileName}' (revision, quality).");
            }
        }

        return resolutions;
    }

    private static bool IsBetterCopy(PackEpisodeCandidate winner, PackEpisodeCandidate runnerUp) =>
        TorrentFileRules.GetRevisionScore(Path.GetFileNameWithoutExtension(winner.FileName)) !=
        TorrentFileRules.GetRevisionScore(Path.GetFileNameWithoutExtension(runnerUp.FileName)) ||
        TorrentFileRules.GetQualityRank(winner.FileName) != TorrentFileRules.GetQualityRank(runnerUp.FileName);

    private static ParsedEpisodeInfo? ParsePackFile(string relativePath, string fileName)
    {
        var parsed = Parser.ParseTitle(fileName);
        if (HasSeasonEpisode(parsed) || IsAbsoluteOnly(parsed))
        {
            return parsed;
        }

        var directory = Path.GetDirectoryName(relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(directory))
        {
            return parsed;
        }

        var folderName = Path.GetFileName(directory);
        var combined = Parser.ParseTitle($"{folderName} {fileName}");
        if (HasSeasonEpisode(combined))
        {
            return combined;
        }

        var folderAsTitle = Parser.ParseTitle($"{folderName}{Path.GetExtension(fileName)}");
        return HasSeasonEpisode(folderAsTitle) ? folderAsTitle : parsed ?? combined ?? folderAsTitle;
    }

    private static bool HasSeasonEpisode(ParsedEpisodeInfo? parsed) =>
        parsed is not null &&
        parsed.EpisodeNumbers.Length > 0 &&
        !parsed.IsDaily &&
        !parsed.Special &&
        !parsed.FullSeason;

    private static bool IsAbsoluteOnly(ParsedEpisodeInfo? parsed) =>
        parsed is not null &&
        parsed.EpisodeNumbers.Length == 0 &&
        parsed.AbsoluteEpisodeNumbers.Length > 0 &&
        !parsed.IsDaily &&
        !parsed.Special &&
        !parsed.FullSeason &&
        !parsed.IsMultiSeason;

    private static (int Season, int Episode, int? LastEpisode)? TryResolve(
        ParsedEpisodeInfo? parsed,
        int seasonNumber,
        IReadOnlySet<int> validEpisodeNumbers,
        string stem,
        InferredEpisodePattern? fallbackPattern,
        AbsoluteEpisodeMapper? absoluteMapper,
        IReadOnlyDictionary<DateOnly, IReadOnlyList<(int Season, int Episode)>>? episodesByAirDate,
        out string? failure)
    {
        failure = null;

        // Sonarr reads a year in a movie name as a season ("Movie.2019" -> S20E19); a season the show
        // does not have is not evidence of anything.
        if (parsed is { EpisodeNumbers.Length: > 0 } &&
            absoluteMapper is { MaxSeasonNumber: > 0 } &&
            parsed.SeasonNumber > absoluteMapper.MaxSeasonNumber)
        {
            failure = $"Name reads as S{parsed.SeasonNumber:00}E{parsed.EpisodeNumbers[0]:00} but the show has {absoluteMapper.MaxSeasonNumber} season(s); likely a year, not linked.";
            return null;
        }

        if (IsYearReadAsSeasonEpisode(parsed, stem))
        {
            failure = $"Name reads as S{parsed!.SeasonNumber:00}E{parsed.EpisodeNumbers[0]:00} only because of the year {parsed.SeasonNumber:00}{parsed.EpisodeNumbers[0]:00}; not linked.";
            return null;
        }

        // Daily releases ("Show.2026.11.10") are matched by TMDB air date (PARSE-15).
        if (parsed is { IsDaily: true } && episodesByAirDate is not null)
        {
            return TryResolveDaily(parsed, episodesByAirDate, out failure);
        }
        var explicitSeasonEpisode = HasSeasonEpisode(parsed) && !parsed!.IsMultiSeason;
        if (explicitSeasonEpisode && TorrentCandidateParser.Parse(stem) is { SeasonNumber: 0, EpisodeNumber: not null })
        {
            failure = "S00 episode in the file name — handled by the specials pipeline.";
            return null;
        }

        if (TryExtractFromParsed(parsed, seasonNumber, validEpisodeNumbers, out var episode))
        {
            return (seasonNumber, episode, GetLastEpisode(parsed!, episode, validEpisodeNumbers));
        }

        // Sonarr read an explicit season/episode that does not fit this group: the pattern inferrer
        // would only guess from character positions, so report instead (PARSE-04).
        if (explicitSeasonEpisode)
        {
            failure = GetSkipReason(parsed, seasonNumber, validEpisodeNumbers);
            if (failure is not null)
            {
                return null;
            }
        }

        // Absolute numbers come before the pattern inferrer: the inferrer only sees character
        // positions and cannot tell "- 11" from "- 01" once a two-digit boundary is crossed.
        if (absoluteMapper is not null && IsAbsoluteOnly(parsed))
        {
            var absolute = parsed!.AbsoluteEpisodeNumbers[0];
            return absoluteMapper.TryMap(absolute, seasonNumber, validEpisodeNumbers, out var mappedSeason, out var mappedEpisode, out failure)
                ? (mappedSeason, mappedEpisode, null)
                : null;
        }

        var inferred = PackEpisodePatternInferrer.TryInferEpisode(stem, seasonNumber, validEpisodeNumbers, fallbackPattern);
        return inferred is null ? null : (seasonNumber, inferred.Value, null);
    }

    private static readonly System.Text.RegularExpressions.Regex YearRegex = new(
        @"(?<![0-9])(?<century>19|20)(?<year>\d{2})(?![0-9])",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    // "Your.Feature.2016" -> Sonarr S20E16, while the name has no SxxEyy of its own.
    private static bool IsYearReadAsSeasonEpisode(ParsedEpisodeInfo? parsed, string stem)
    {
        if (parsed is not { EpisodeNumbers.Length: > 0 } ||
            TorrentCandidateParser.Parse(stem) is { SeasonNumber: not null, EpisodeNumber: not null })
        {
            return false;
        }

        return YearRegex.Matches(stem).Any(match =>
            int.TryParse(match.Groups["century"].Value, out var century) && century == parsed.SeasonNumber &&
            int.TryParse(match.Groups["year"].Value, out var year) && year == parsed.EpisodeNumbers[0]);
    }

    private static (int Season, int Episode, int? LastEpisode)? TryResolveDaily(
        ParsedEpisodeInfo parsed,
        IReadOnlyDictionary<DateOnly, IReadOnlyList<(int Season, int Episode)>> episodesByAirDate,
        out string? failure)
    {
        if (!DateOnly.TryParse(parsed.AirDate, System.Globalization.CultureInfo.InvariantCulture, out var airDate))
        {
            failure = $"Daily release with unreadable air date '{parsed.AirDate}'.";
            return null;
        }

        if (!episodesByAirDate.TryGetValue(airDate, out var aired) || aired.Count == 0)
        {
            failure = $"Daily release {airDate:yyyy-MM-dd}: no TMDB episode aired that day.";
            return null;
        }

        if (aired.Count > 1)
        {
            failure = $"Daily release {airDate:yyyy-MM-dd}: {aired.Count} TMDB episodes aired that day; not guessing which.";
            return null;
        }

        failure = null;
        return (aired[0].Season, aired[0].Episode, null);
    }

    /// <summary>
    /// Multi-episode file (Sonarr EpisodeNumbers [1, 2]): the last episode of the run that starts at
    /// the first number, stops at a gap and stays inside TMDB (PARSE-06).
    /// </summary>
    private static int? GetLastEpisode(ParsedEpisodeInfo parsed, int firstEpisode, IReadOnlySet<int> validEpisodeNumbers)
    {
        var numbers = parsed.EpisodeNumbers.Distinct().Order().ToList();
        var last = firstEpisode;
        foreach (var number in numbers.Where(number => number > firstEpisode))
        {
            if (number != last + 1 || !validEpisodeNumbers.Contains(number))
            {
                break;
            }

            last = number;
        }

        return last > firstEpisode ? last : null;
    }

    private static string? GetSkipReason(
        ParsedEpisodeInfo? parsed,
        int seasonNumber,
        IReadOnlySet<int> validEpisodeNumbers)
    {
        if (parsed?.IsDaily == true)
        {
            return "Daily episode release — not matched to TMDB episode number.";
        }

        if (parsed?.Special == true || parsed?.FullSeason == true)
        {
            return "Special or season pack — handled by specials pipeline.";
        }

        if (parsed?.IsMultiSeason == true)
        {
            return "Multi-season release title.";
        }

        if (parsed?.EpisodeNumbers.Length > 0)
        {
            if (parsed.SeasonNumber > 0 && parsed.SeasonNumber != seasonNumber)
            {
                return $"Season mismatch (parsed S{parsed.SeasonNumber:00}, folder S{seasonNumber:00}).";
            }

            var candidate = parsed.EpisodeNumbers[0];
            if (!validEpisodeNumbers.Contains(candidate))
            {
                return $"Episode {candidate} not in TMDB for S{seasonNumber:00}.";
            }
        }

        return null;
    }

    private static bool TryExtractFromParsed(
        ParsedEpisodeInfo? parsed,
        int seasonNumber,
        IReadOnlySet<int> validEpisodeNumbers,
        out int episode)
    {
        episode = 0;
        if (parsed is null ||
            parsed.IsDaily ||
            parsed.Special ||
            parsed.FullSeason ||
            parsed.IsMultiSeason ||
            parsed.EpisodeNumbers.Length == 0)
        {
            return false;
        }

        if (parsed.SeasonNumber > 0 && parsed.SeasonNumber != seasonNumber)
        {
            return false;
        }

        episode = parsed.EpisodeNumbers[0];
        return validEpisodeNumbers.Contains(episode);
    }

    private static long GetFileSize(IReadOnlyDictionary<string, long>? fileSizes, string relativePath) =>
        fileSizes is not null && fileSizes.TryGetValue(relativePath, out var size) ? size : 0;
}

public sealed record PackEpisodeCandidate(string RelativePath, string FileName, int Season, int Episode, int? LastEpisode = null);
