using media_management_app.Models;

namespace media_management_app.Services;

public static class PackSeasonFileGrouper
{
    public const int FlatSeasonKey = 0;

    public sealed class SeasonFileGroup
    {
        public int SeasonNumber { get; init; }

        public List<(string RelativePath, string FileName)> Files { get; init; } = [];
    }

    public static IReadOnlyList<SeasonFileGroup> Group(
        IReadOnlyList<(string RelativePath, string FileName)> files,
        PackFolderTreeAnalysis? tree)
    {
        var groups = new Dictionary<int, SeasonFileGroup>();
        var flatFiles = new List<(string RelativePath, string FileName)>();

        foreach (var file in files)
        {
            if (IsExcludedFromEpisodeGroup(file.RelativePath, file.FileName))
            {
                continue;
            }

            var seasonHint = tree?.PathSeasonHints.TryGetValue(file.RelativePath, out var hint) == true
                ? hint
                : TorrentCandidateParser.TryGetSeasonHintFromPath(file.RelativePath);

            if (seasonHint is > 0)
            {
                if (!groups.TryGetValue(seasonHint.Value, out var group))
                {
                    group = new SeasonFileGroup { SeasonNumber = seasonHint.Value };
                    groups[seasonHint.Value] = group;
                }

                group.Files.Add(file);
                continue;
            }

            flatFiles.Add(file);
        }

        foreach (var file in flatFiles)
        {
            var parsed = TorrentCandidateParser.Parse(file.FileName, file.RelativePath);
            var season = parsed.SeasonNumber is > 0 ? parsed.SeasonNumber.Value : FlatSeasonKey;
            if (!groups.TryGetValue(season, out var group))
            {
                group = new SeasonFileGroup { SeasonNumber = season };
                groups[season] = group;
            }

            group.Files.Add(file);
        }

        return groups.Values.OrderBy(group => group.SeasonNumber).ToList();
    }

    public static bool IsExcludedFromEpisodeGroup(string relativePath, string fileName)
    {
        if (IsUnderMoviesFolder(relativePath) ||
            fileName.Contains("movie", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IsUnderExtrasFolder(relativePath))
        {
            return true;
        }

        if (PackSpecialBucketDetector.IsSpecialBucketCandidate(relativePath, fileName))
        {
            return true;
        }

        var parsed = TorrentCandidateParser.Parse(fileName, relativePath);
        return parsed.IsExtraContent;
    }

    private static bool IsUnderMoviesFolder(string relativePath) =>
        PathContainsFolder(relativePath, "movies") || PathContainsFolder(relativePath, "films");

    private static bool IsUnderExtrasFolder(string relativePath) =>
        PathContainsFolder(relativePath, "extras") || PathContainsFolder(relativePath, "extra");

    private static bool PathContainsFolder(string relativePath, string folderName) =>
        relativePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => string.Equals(segment, folderName, StringComparison.OrdinalIgnoreCase));
}
