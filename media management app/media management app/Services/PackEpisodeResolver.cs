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
        AbsoluteEpisodeMapper? absoluteMapper = null)
    {
        var candidates = new List<PackEpisodeCandidate>();
        var unresolved = new List<(string RelativePath, string Reason)>();

        foreach (var (relativePath, fileName) in files)
        {
            if (PackSeasonFileGrouper.IsExcludedFromEpisodeGroup(relativePath, fileName))
            {
                continue;
            }

            var stem = Path.GetFileNameWithoutExtension(fileName);
            var parsed = ParsePackFile(relativePath, fileName);
            var resolved = TryResolve(parsed, seasonNumber, validEpisodeNumbers, stem, fallbackPattern, absoluteMapper, out var failure);
            if (resolved is { } target)
            {
                candidates.Add(new PackEpisodeCandidate(relativePath, fileName, target.Season, target.Episode));
                continue;
            }

            unresolved.Add((relativePath, failure ?? GetSkipReason(parsed, seasonNumber, validEpisodeNumbers) ?? "No episode pattern matched TMDB season set."));
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

            resolutions[winner.RelativePath] = PackEpisodeResolution.Linked(season, episode);
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

    private static (int Season, int Episode)? TryResolve(
        ParsedEpisodeInfo? parsed,
        int seasonNumber,
        IReadOnlySet<int> validEpisodeNumbers,
        string stem,
        InferredEpisodePattern? fallbackPattern,
        AbsoluteEpisodeMapper? absoluteMapper,
        out string? failure)
    {
        failure = null;
        if (TryExtractFromParsed(parsed, seasonNumber, validEpisodeNumbers, out var episode))
        {
            return (seasonNumber, episode);
        }

        // Absolute numbers come before the pattern inferrer: the inferrer only sees character
        // positions and cannot tell "- 11" from "- 01" once a two-digit boundary is crossed.
        if (absoluteMapper is not null && IsAbsoluteOnly(parsed))
        {
            var absolute = parsed!.AbsoluteEpisodeNumbers[0];
            return absoluteMapper.TryMap(absolute, seasonNumber, validEpisodeNumbers, out var mappedSeason, out var mappedEpisode, out failure)
                ? (mappedSeason, mappedEpisode)
                : null;
        }

        var inferred = PackEpisodePatternInferrer.TryInferEpisode(stem, seasonNumber, validEpisodeNumbers, fallbackPattern);
        return inferred is null ? null : (seasonNumber, inferred.Value);
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

public sealed record PackEpisodeCandidate(string RelativePath, string FileName, int Season, int Episode);
