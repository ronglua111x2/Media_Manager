using System.Text.RegularExpressions;
using media_management_app.Models;

namespace media_management_app.Services;

public static class PackFolderTreeAnalyzer
{
    private static readonly Regex ExtrasFolderRegex = new(
        @"^(extras?|nced|ncop|menus?|pvs?)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SpecialsFolderRegex = new(
        @"^(specials?|ova?s?|season\s*00)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MoviesFolderRegex = new(
        @"^(movies?|films?)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static PackFolderTreeAnalysis Analyze(IReadOnlyList<string> relativePaths)
    {
        var folderSeasons = new SortedSet<int>();
        var pathHints = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        var hasExtras = false;
        var hasSpecials = false;
        var hasMovies = false;

        foreach (var relativePath in relativePaths)
        {
            var normalizedPath = NormalizePath(relativePath);
            pathHints[relativePath] = TorrentCandidateParser.TryGetSeasonHintFromPath(normalizedPath);

            foreach (var segment in normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries).SkipLast(1))
            {
                if (ExtrasFolderRegex.IsMatch(segment))
                {
                    hasExtras = true;
                }

                if (SpecialsFolderRegex.IsMatch(segment))
                {
                    hasSpecials = true;
                }

                if (MoviesFolderRegex.IsMatch(segment))
                {
                    hasMovies = true;
                }

                var season = TorrentCandidateParser.TryParseSeasonFolderSegment(segment);
                if (season is > 0)
                {
                    folderSeasons.Add(season.Value);
                }
            }
        }

        return new PackFolderTreeAnalysis
        {
            FolderCoveredSeasons = folderSeasons.ToList(),
            PathSeasonHints = pathHints,
            HasExtrasFolder = hasExtras,
            HasSpecialsFolder = hasSpecials,
            HasMoviesFolder = hasMovies
        };
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}
