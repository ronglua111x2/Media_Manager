using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public static class PackSpecialEpisodeMapper
{
    public static IReadOnlyDictionary<string, TrackedEpisode?> MapBucket(
        PackSpecialBucket bucket,
        InferredSpecialPattern pattern,
        IReadOnlyList<TrackedEpisode> specialsEpisodes,
        IReadOnlySet<int> claimedSpecialEpisodes)
    {
        var results = new Dictionary<string, TrackedEpisode?>(StringComparer.OrdinalIgnoreCase);
        var claimed = claimedSpecialEpisodes is not null
            ? new HashSet<int>(claimedSpecialEpisodes)
            : new HashSet<int>();
        var candidates = specialsEpisodes
            .Where(episode => episode.SeasonNumber == AppConstants.SpecialsSeasonNumber)
            .OrderBy(episode => episode.EpisodeNumber)
            .ToList();

        if (candidates.Count == 0)
        {
            foreach (var file in bucket.Files)
            {
                results[file.RelativePath] = null;
            }

            return results;
        }

        var sortedFiles = bucket.Files
            .OrderBy(file => GetSortKey(file.FileName, pattern))
            .ThenBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var useOrdinalFallback = sortedFiles.Count == candidates.Count;

        for (var index = 0; index < sortedFiles.Count; index++)
        {
            var (relativePath, fileName) = sortedFiles[index];
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var parsed = TorrentCandidateParser.Parse(fileName, relativePath);

            var titleMatch = SpecialEpisodeTitleMatcher.TryMatch(parsed, candidates, claimed);
            if (titleMatch is not null)
            {
                results[relativePath] = titleMatch;
                claimed.Add(titleMatch.EpisodeNumber);
                continue;
            }

            var inferredIndex = PackSpecialPatternInferrer.TryExtractIndex(stem, pattern) ??
                                parsed.ReleaseSpecialIndex ??
                                TorrentCandidateParser.TryParseBareEpisodeIndex(fileName);

            if (inferredIndex is > 0)
            {
                var indexMatch = candidates.FirstOrDefault(episode => episode.EpisodeNumber == inferredIndex.Value);
                if (indexMatch is not null && !claimed.Contains(indexMatch.EpisodeNumber))
                {
                    results[relativePath] = indexMatch;
                    claimed.Add(indexMatch.EpisodeNumber);
                    continue;
                }
            }

            if (useOrdinalFallback && index < candidates.Count)
            {
                var ordinalMatch = candidates[index];
                if (!claimed.Contains(ordinalMatch.EpisodeNumber))
                {
                    results[relativePath] = ordinalMatch;
                    claimed.Add(ordinalMatch.EpisodeNumber);
                    continue;
                }
            }

            results[relativePath] = null;
        }

        return results;
    }

    private static int GetSortKey(string fileName, InferredSpecialPattern pattern)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        return PackSpecialPatternInferrer.TryExtractIndex(stem, pattern) ??
               TorrentCandidateParser.TryParseBareEpisodeIndex(fileName) ??
               int.MaxValue;
    }
}
