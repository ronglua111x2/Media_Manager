using System.Text.RegularExpressions;
using media_management_app.Models;

namespace media_management_app.Services;

public static class PackContentAnalyzer
{
    private static readonly Regex SeasonSpecialFileRegex = new(
        @"\bS(?<season>\d{1,2})S(?<special>\d{1,2})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SeasonOvaFileRegex = new(
        @"\bS(?<season>\d{1,2})OVA\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static PackContentProfile Analyze(
        string torrentTitle,
        TorrentMetadataProbeResult? probe,
        IReadOnlyList<int>? coveredSeasonsOverride = null)
    {
        var titleSeasons = TorrentCandidateParser.Parse(torrentTitle).CoveredSeasons;
        var probeSeasons = probe is { IsAvailable: true }
            ? InferCoveredSeasonsFromProbe(probe).ToList()
            : [];
        var coveredSeasons = MergeSeasons(titleSeasons, probeSeasons, coveredSeasonsOverride);

        var titleIncludesSpecials = ContainsKeyword(torrentTitle, "special");
        var titleIncludesOva = ContainsKeyword(torrentTitle, "ova") ||
                               ContainsKeyword(torrentTitle, "oad") ||
                               ContainsKeyword(torrentTitle, "oav");
        var titleIncludesMovies = ContainsKeyword(torrentTitle, "movie") || ContainsKeyword(torrentTitle, "film");
        var titleIsComplete = ContainsKeyword(torrentTitle, "complete") || ContainsKeyword(torrentTitle, "batch");

        var movieFileNames = new List<string>();
        var specialFileNames = new List<string>();
        var probeIncludesSpecials = false;
        var probeIncludesOva = false;
        var probeIncludesMovies = false;

        if (probe is { IsAvailable: true })
        {
            foreach (var file in probe.VideoFiles)
            {
                var fileName = Path.GetFileName(file.Path);
                var normalizedPath = NormalizePath(file.Path);

                if (IsMoviePath(normalizedPath, fileName))
                {
                    probeIncludesMovies = true;
                    if (!string.IsNullOrWhiteSpace(fileName))
                    {
                        movieFileNames.Add(fileName);
                    }

                    continue;
                }

                if (IsExtraFile(normalizedPath, fileName))
                {
                    probeIncludesSpecials = true;
                    if (!string.IsNullOrWhiteSpace(fileName))
                    {
                        specialFileNames.Add(fileName);
                    }

                    continue;
                }

                if (IsSpecialFile(normalizedPath, fileName))
                {
                    probeIncludesSpecials = true;
                    if (SeasonOvaFileRegex.IsMatch(fileName))
                    {
                        probeIncludesOva = true;
                    }

                    if (!string.IsNullOrWhiteSpace(fileName))
                    {
                        specialFileNames.Add(fileName);
                    }
                }
            }
        }

        return new PackContentProfile
        {
            CoveredSeasons = coveredSeasons.ToList(),
            IncludesSpecials = titleIncludesSpecials || probeIncludesSpecials,
            IncludesOva = titleIncludesOva || probeIncludesOva,
            IncludesMovies = titleIncludesMovies || probeIncludesMovies,
            IsCompleteBundle = titleIsComplete,
            MovieFileNames = movieFileNames
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            SpecialFileNames = specialFileNames
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static IReadOnlyList<int> MergeSeasons(
        IReadOnlyList<int> fromTitle,
        IReadOnlyList<int> fromProbe,
        IReadOnlyList<int>? overrideSeasons)
    {
        if (overrideSeasons is { Count: > 0 })
        {
            return overrideSeasons.Distinct().Order().ToList();
        }

        return fromTitle
            .Concat(fromProbe)
            .Distinct()
            .Order()
            .ToList();
    }

    private static IEnumerable<int> InferCoveredSeasonsFromProbe(TorrentMetadataProbeResult probe)
    {
        return probe.VideoFiles
            .Select(file => TorrentCandidateParser.Parse($"{probe.TorrentName} {file.Path}").SeasonNumber)
            .Where(season => season is >= 0)
            .Select(season => season!.Value)
            .Distinct()
            .Order();
    }

    private static bool IsMoviePath(string normalizedPath, string fileName)
    {
        return PathContainsFolder(normalizedPath, "movies") ||
               ContainsKeyword(fileName, "movie");
    }

    private static bool IsExtraFile(string normalizedPath, string fileName)
    {
        return PathContainsFolder(normalizedPath, "extras") ||
               Regex.IsMatch(fileName, @"\b(?:NCED|NCOP)\b", RegexOptions.IgnoreCase);
    }

    private static bool IsSpecialFile(string normalizedPath, string fileName)
    {
        return PathContainsFolder(normalizedPath, "specials") ||
               PathContainsOvaFolder(normalizedPath) ||
               SeasonSpecialFileRegex.IsMatch(fileName) ||
               SeasonOvaFileRegex.IsMatch(fileName) ||
               ContainsKeyword(fileName, "special") ||
               ContainsKeyword(fileName, "ova");
    }

    private static bool PathContainsOvaFolder(string normalizedPath)
    {
        return normalizedPath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment.StartsWith("OVA", StringComparison.OrdinalIgnoreCase));
    }

    private static bool PathContainsFolder(string normalizedPath, string folderName)
    {
        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => string.Equals(segment, folderName, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static bool ContainsKeyword(string value, string keyword) =>
        Regex.IsMatch(value, $@"\b{Regex.Escape(keyword)}s?\b", RegexOptions.IgnoreCase);
}
