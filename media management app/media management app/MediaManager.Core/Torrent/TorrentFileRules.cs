using System.Text.RegularExpressions;
using media_management_app.Models;

namespace media_management_app.Services;

/// <summary>
/// Rules for picking video files inside a torrent, shared by the single-episode link, the pack
/// link and pack inspect: which extensions are video, which files are samples/proofs, and which
/// copy wins when several files are the same episode.
/// </summary>
public static class TorrentFileRules
{
    public static IReadOnlyList<string> VideoExtensions { get; } =
        [".mkv", ".mp4", ".avi", ".mov", ".wmv", ".m4v", ".ts", ".m2ts", ".webm"];

    private static readonly HashSet<string> VideoExtensionSet =
        new(VideoExtensions, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> SampleFolderNames =
        new(["sample", "samples", "proof", "proofs"], StringComparer.OrdinalIgnoreCase);

    // Scene naming: "<release>-sample", "<release>.sample", "sample-<release>", "<release>-proof".
    // A bare word inside the title ("Proof.S01E01", "The.Sample.Case") is not enough.
    private static readonly Regex SampleStemRegex = new(
        @"[\s._\-]sample$|^sample-|-proof$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "v2"/"proper"/"repack" as a separate token, or "v2" glued to the episode number
    // ("S01E03v2", "- 05v2", "E05v2").
    private static readonly Regex RevisionRegex = new(
        @"(?:^|[^a-z0-9])(?:v(?<version>\d+)|proper|repack)(?:$|[^a-z0-9])" +
        @"|(?<=(?:^|[^a-z0-9])(?:s\d{1,3}e|ep?)?\d{1,4})v(?<version>\d{1,2})(?=$|[^a-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsVideoFile(string path) =>
        !string.IsNullOrWhiteSpace(path) && VideoExtensionSet.Contains(Path.GetExtension(path));

    public static bool IsSampleOrProof(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        var segments = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        if (segments.SkipLast(1).Any(SampleFolderNames.Contains))
        {
            return true;
        }

        var stem = Path.GetFileNameWithoutExtension(segments[^1]);
        return SampleStemRegex.IsMatch(stem);
    }

    public static int GetRevisionScore(string fileNameOrStem)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrStem))
        {
            return 0;
        }

        var match = RevisionRegex.Match(fileNameOrStem);
        if (!match.Success)
        {
            return 0;
        }

        return match.Groups["version"].Success && int.TryParse(match.Groups["version"].Value, out var version)
            ? 100 + version
            : 50;
    }

    public static int GetQualityRank(string fileName) =>
        TorrentQuality.GetRank(TorrentQuality.Detect(fileName));

    /// <summary>
    /// Orders copies of the same episode from best to worst: revision (v2/proper/repack), then
    /// quality, then size, then path so the result never depends on the input order.
    /// </summary>
    public static IOrderedEnumerable<T> OrderByPreference<T>(
        IEnumerable<T> items,
        Func<T, string> relativePath,
        Func<T, long> size) =>
        items
            .OrderByDescending(item => GetRevisionScore(Path.GetFileNameWithoutExtension(relativePath(item))))
            .ThenByDescending(item => GetQualityRank(Path.GetFileName(relativePath(item))))
            .ThenByDescending(size)
            .ThenBy(relativePath, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Picks the file of a multi-file torrent that is the given episode. Samples are ignored; the
    /// file name is parsed first and the whole path only when the name has no episode number.
    /// </summary>
    public static TorrentContentFile? SelectEpisodeFile(
        IReadOnlyList<TorrentContentFile> files,
        int seasonNumber,
        int episodeNumber)
    {
        var candidates = files.Where(file => !IsSampleOrProof(file.Name)).ToList();
        var byName = candidates
            .Where(file => IsEpisode(ParseName(file.Name), seasonNumber, episodeNumber))
            .ToList();
        if (byName.Count > 0)
        {
            return OrderByPreference(byName, file => file.Name, file => file.Size).First();
        }

        // Only the folder names the episode. A folder named for several episodes (S01E01-E03) is a range for the
        // folder, not for each file in it: pick the file by its own number, or none (audit L1-M1).
        var byPath = candidates
            .Where(file => !HasSeasonEpisode(ParseName(file.Name)))
            .Select(file => (File: file, Parsed: TorrentCandidateParser.Parse(file.Name)))
            .Where(item => IsEpisode(item.Parsed, seasonNumber, episodeNumber))
            .ToList();
        if (byPath.Count == 0)
        {
            return null;
        }

        if (byPath.Any(item => item.Parsed.LastEpisodeNumber is not null))
        {
            var byOwnNumber = byPath
                .Select(item => item.File)
                .Where(file => TorrentCandidateParser.TryParseBareEpisodeIndex(Path.GetFileName(file.Name.Replace('\\', '/'))) == episodeNumber)
                .ToList();
            return byOwnNumber.Count == 0
                ? null
                : OrderByPreference(byOwnNumber, file => file.Name, file => file.Size).First();
        }

        return OrderByPreference(byPath.Select(item => item.File).ToList(), file => file.Name, file => file.Size).First();
    }

    private static TorrentCandidateParseResult ParseName(string relativePath) =>
        TorrentCandidateParser.Parse(Path.GetFileName(relativePath.Replace('\\', '/')), relativePath);

    private static bool HasSeasonEpisode(TorrentCandidateParseResult parsed) =>
        parsed.SeasonNumber is not null && parsed.EpisodeNumber is not null;

    private static bool IsEpisode(TorrentCandidateParseResult parsed, int seasonNumber, int episodeNumber) =>
        parsed.SeasonNumber == seasonNumber &&
        parsed.EpisodeNumber is { } first &&
        episodeNumber >= first &&
        episodeNumber <= (parsed.LastEpisodeNumber ?? first);
}
