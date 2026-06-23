using media_management_app.Models;

namespace media_management_app.Services;

public static class PackSpecialBucketDetector
{
    public static IReadOnlyList<PackSpecialBucket> Detect(
        IReadOnlyList<(string RelativePath, string FileName)> files,
        PackFolderTreeAnalysis? tree)
    {
        var buckets = new Dictionary<(SpecialLayoutKind Kind, int? ParentSeason), PackSpecialBucket>();

        foreach (var file in files)
        {
            var layout = ClassifyLayout(file.RelativePath, file.FileName);
            if (layout is null)
            {
                continue;
            }

            var key = (layout.Value.Kind, layout.Value.ParentSeason);
            if (!buckets.TryGetValue(key, out var bucket))
            {
                bucket = new PackSpecialBucket
                {
                    LayoutKind = layout.Value.Kind,
                    ParentSeasonNumber = layout.Value.ParentSeason
                };
                buckets[key] = bucket;
            }

            bucket.Files.Add(file);
        }

        return buckets.Values.ToList();
    }

    public static bool IsSpecialBucketCandidate(string relativePath, string fileName)
    {
        return ClassifyLayout(relativePath, fileName) is not null;
    }

    public static bool IsMixedInSeasonSpecial(string relativePath, string fileName) =>
        ClassifyLayout(relativePath, fileName)?.Kind == SpecialLayoutKind.MixedInSeason;

    private static (SpecialLayoutKind Kind, int? ParentSeason)? ClassifyLayout(string relativePath, string fileName)
    {
        if (IsUnderMoviesFolder(relativePath) ||
            fileName.Contains("movie", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (IsUnderTorrentExtrasFolder(relativePath))
        {
            return null;
        }

        var parsed = TorrentCandidateParser.Parse(fileName, relativePath);
        if (parsed.IsExtraContent)
        {
            return null;
        }

        if (TryGetSeasonNestedSpecial(relativePath, out var nestedSeason))
        {
            return (SpecialLayoutKind.SeasonNestedSpecial, nestedSeason);
        }

        if (IsRootLevelOvaOrSpecialsFolder(relativePath))
        {
            return (SpecialLayoutKind.RootOvaFolder, null);
        }

        if (TryGetMixedInSeasonSpecial(relativePath, fileName, parsed, out var mixedSeason))
        {
            return (SpecialLayoutKind.MixedInSeason, mixedSeason);
        }

        return null;
    }

    private static bool TryGetSeasonNestedSpecial(string relativePath, out int parentSeason)
    {
        parentSeason = 0;
        var segments = NormalizeSegments(relativePath);
        if (segments.Length < 3)
        {
            return false;
        }

        for (var index = 1; index < segments.Length - 1; index++)
        {
            if (!IsOvaOrSpecialsSegment(segments[index]))
            {
                continue;
            }

            var season = TorrentCandidateParser.TryParseSeasonFolderSegment(segments[index - 1]);
            if (season is > 0)
            {
                parentSeason = season.Value;
                return true;
            }
        }

        return false;
    }

    private static bool IsRootLevelOvaOrSpecialsFolder(string relativePath)
    {
        var segments = NormalizeSegments(relativePath);
        if (segments.Length < 2)
        {
            return false;
        }

        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (!IsOvaOrSpecialsSegment(segments[index]))
            {
                continue;
            }

            for (var prior = 0; prior < index; prior++)
            {
                if (TorrentCandidateParser.TryParseSeasonFolderSegment(segments[prior]) is > 0)
                {
                    return false;
                }
            }

            return true;
        }

        return false;
    }

    private static bool TryGetMixedInSeasonSpecial(
        string relativePath,
        string fileName,
        TorrentCandidateParseResult parsed,
        out int parentSeason)
    {
        parentSeason = 0;
        var seasonHint = TorrentCandidateParser.TryGetSeasonHintFromPath(relativePath)
            ?? (parsed.ReleaseSeasonHint is > 0 ? parsed.ReleaseSeasonHint : null);
        if (seasonHint is null or <= 0)
        {
            return false;
        }

        if (IsRootLevelOvaOrSpecialsFolder(relativePath) || TryGetSeasonNestedSpecial(relativePath, out _))
        {
            return false;
        }

        if (!HasMixedInSeasonSignals(fileName, relativePath, parsed, seasonHint.Value))
        {
            return false;
        }

        parentSeason = seasonHint.Value;
        return true;
    }

    private static bool HasMixedInSeasonSignals(
        string fileName,
        string relativePath,
        TorrentCandidateParseResult parsed,
        int seasonHint)
    {
        if (parsed.IsSpecialContent)
        {
            return true;
        }

        if (parsed.ReleaseSeasonHint == seasonHint && parsed.ReleaseSpecialIndex is not null)
        {
            return true;
        }

        if (parsed.SeasonNumber == seasonHint &&
            parsed.EpisodeNumber is null &&
            (fileName.Contains("OVA", StringComparison.OrdinalIgnoreCase) ||
             fileName.Contains("special", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return TorrentCandidateParser.TryParseBareEpisodeIndex(fileName) is not null &&
               (fileName.Contains("OVA", StringComparison.OrdinalIgnoreCase) ||
                fileName.Contains("special", StringComparison.OrdinalIgnoreCase));
    }

    private static string[] NormalizeSegments(string relativePath) =>
        relativePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static bool IsOvaOrSpecialsSegment(string segment) =>
        segment.StartsWith("OVA", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("specials", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("special", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("season 00", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("season00", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnderMoviesFolder(string relativePath) =>
        PathContainsFolder(relativePath, "movies") || PathContainsFolder(relativePath, "films");

    private static bool IsUnderTorrentExtrasFolder(string relativePath) =>
        PathContainsFolder(relativePath, "extras") || PathContainsFolder(relativePath, "extra");

    private static bool PathContainsFolder(string relativePath, string folderName) =>
        relativePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => string.Equals(segment, folderName, StringComparison.OrdinalIgnoreCase));
}
