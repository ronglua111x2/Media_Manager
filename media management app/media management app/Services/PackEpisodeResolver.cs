using System.Text.RegularExpressions;
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
        IReadOnlyDictionary<string, long>? fileSizes = null)
    {
        var candidates = new List<(string RelativePath, string FileName, int Episode)>();

        foreach (var (relativePath, fileName) in files)
        {
            if (PackSeasonFileGrouper.IsExcludedFromEpisodeGroup(relativePath, fileName))
            {
                continue;
            }

            var stem = Path.GetFileNameWithoutExtension(fileName);
            var parsed = ParsePackFile(relativePath, fileName);
            var episode = ExtractEpisodeNumber(parsed, seasonNumber, validEpisodeNumbers, stem, fallbackPattern);

            if (episode is null)
            {
                continue;
            }

            candidates.Add((relativePath, fileName, episode.Value));
        }

        var resolutions = new Dictionary<string, PackEpisodeResolution>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in candidates.GroupBy(item => item.Episode))
        {
            var winner = TorrentFileRules.OrderByPreference(
                    group,
                    item => item.RelativePath,
                    item => GetFileSize(fileSizes, item.RelativePath))
                .First();

            resolutions[winner.RelativePath] = PackEpisodeResolution.Linked(seasonNumber, winner.Episode);

            foreach (var loser in group.Where(item => !string.Equals(item.RelativePath, winner.RelativePath, StringComparison.OrdinalIgnoreCase)))
            {
                resolutions[loser.RelativePath] = PackEpisodeResolution.Skipped(
                    seasonNumber,
                    $"Duplicate of S{seasonNumber:00}E{loser.Episode:00}; kept '{winner.FileName}' (revision, quality, size).");
            }
        }

        foreach (var (relativePath, fileName) in files)
        {
            if (resolutions.ContainsKey(relativePath) ||
                PackSeasonFileGrouper.IsExcludedFromEpisodeGroup(relativePath, fileName))
            {
                continue;
            }

            var stem = Path.GetFileNameWithoutExtension(fileName);
            var parsed = ParsePackFile(relativePath, fileName);
            var skipReason = GetSkipReason(parsed, seasonNumber, validEpisodeNumbers, stem, fallbackPattern)
                ?? "No episode pattern matched TMDB season set.";

            resolutions[relativePath] = PackEpisodeResolution.Skipped(seasonNumber, skipReason);
        }

        return resolutions;
    }

    private static ParsedEpisodeInfo? ParsePackFile(string relativePath, string fileName)
    {
        var parsed = Parser.ParseTitle(fileName);
        if (HasSeasonEpisode(parsed))
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

    private static int? ExtractEpisodeNumber(
        ParsedEpisodeInfo? parsed,
        int seasonNumber,
        IReadOnlySet<int> validEpisodeNumbers,
        string stem,
        InferredEpisodePattern? fallbackPattern)
    {
        if (TryExtractFromParsed(parsed, seasonNumber, validEpisodeNumbers, out var episode))
        {
            return episode;
        }

        return PackEpisodePatternInferrer.TryInferEpisode(stem, seasonNumber, validEpisodeNumbers, fallbackPattern);
    }

    private static string? GetSkipReason(
        ParsedEpisodeInfo? parsed,
        int seasonNumber,
        IReadOnlySet<int> validEpisodeNumbers,
        string stem,
        InferredEpisodePattern? fallbackPattern)
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
            var parsedSeason = parsed.SeasonNumber > 0 ? parsed.SeasonNumber : seasonNumber;
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

        if (PackEpisodePatternInferrer.TryInferEpisode(stem, seasonNumber, validEpisodeNumbers, fallbackPattern) is not null)
        {
            return null;
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
