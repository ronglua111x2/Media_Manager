using System.Text.RegularExpressions;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

/// <summary>
/// Matches a special/OVA file to a TMDB Season 0 entry by its title. Only meaningful words count: stopwords,
/// the show's own name, "OVA"/"special", release tags, file extensions, group tags in brackets and anything with
/// a digit are dropped from both sides. A match needs at least <see cref="MinimumShare"/> of the TMDB title's
/// meaningful words, and two entries with the same best score mean no match (parser audit SPEC-03). Numbers in
/// a TMDB title must appear in the file name for that entry to count (audit L2-01).
/// </summary>
public static class SpecialEpisodeTitleMatcher
{
    public const double MinimumShare = 0.6;

    private static readonly Regex BracketGroupRegex = new(@"\[[^\]]*\]|\{[^}]*\}", RegexOptions.Compiled);

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "of", "and", "or", "to", "in", "on", "at", "for", "with", "from", "by", "as", "into",
        "is", "are", "was", "be", "it", "its", "this", "that", "vs", "so", "but", "not", "no", "my", "your",
        "our", "his", "her", "their", "we", "you", "me", "wa", "ga", "wo", "ni", "de"
    };

    private static readonly HashSet<string> NoiseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "ova", "ovas", "oad", "ona", "special", "specials", "sp", "extra", "extras", "episode", "ep",
        "mkv", "mp4", "avi", "m2ts", "ts", "webm", "wmv",
        "bd", "bdrip", "bluray", "web", "webrip", "webdl", "dl", "hevc", "avc", "aac", "flac", "opus", "dts",
        "dual", "audio", "multi", "sub", "subs", "subbed", "dub", "dubbed", "eng", "raw", "remux", "hdr", "sdr",
        "proper", "repack", "batch", "uncensored", "uncut"
    };

    /// <summary>
    /// Best title match among <paramref name="specialsEpisodes"/>, or null when nothing reaches the threshold, the
    /// best score is shared, or the best entry is already claimed.
    /// </summary>
    public static TrackedEpisode? TryMatch(
        string fileName,
        string? showTitle,
        IReadOnlyList<TrackedEpisode> specialsEpisodes,
        IReadOnlySet<int>? claimedEpisodeNumbers = null)
    {
        var fileTokens = FileTitleTokens(fileName, showTitle);
        if (fileTokens.Count == 0)
        {
            return null;
        }

        var best = BestMatch(fileName, fileTokens, showTitle, specialsEpisodes);
        return best is null || (claimedEpisodeNumbers is not null && claimedEpisodeNumbers.Contains(best.EpisodeNumber))
            ? null
            : best;
    }

    /// <summary>
    /// Inspect-path overload (no show title known). Title first, then the special index the parser read
    /// (<c>S1S03</c>, <c>OVA 03</c>) as a TMDB episode number, as before.
    /// </summary>
    public static TrackedEpisode? TryMatch(
        TorrentCandidateParseResult parsed,
        IReadOnlyList<TrackedEpisode> specialsEpisodes,
        IReadOnlySet<int>? claimedEpisodeNumbers = null)
    {
        var candidates = specialsEpisodes
            .Where(episode => episode.SeasonNumber == AppConstants.SpecialsSeasonNumber)
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var titleMatch = TryMatch(parsed.RawTitle, showTitle: null, candidates, claimedEpisodeNumbers);
        if (titleMatch is not null)
        {
            return titleMatch;
        }

        var index = ResolveSpecialIndex(parsed);
        if (index is > 0)
        {
            var indexMatch = candidates.FirstOrDefault(episode => episode.EpisodeNumber == index.Value);
            if (indexMatch is not null && !IsClaimed(indexMatch.EpisodeNumber, claimedEpisodeNumbers))
            {
                return indexMatch;
            }
        }

        return null;
    }

    /// <summary>
    /// True when the file's own title names this TMDB entry and no other one (used to trust a <c>S00Exx</c> file
    /// name). Numbers in the TMDB title ("Part 2", "2019") must also be in the file name, because the words alone
    /// cannot tell "Part 1" from "Part 2" (audit L2-01).
    /// </summary>
    public static bool Confirms(
        string fileName,
        string? showTitle,
        TrackedEpisode episode,
        IReadOnlyList<TrackedEpisode> specialsEpisodes)
    {
        var fileTokens = FileTitleTokens(fileName, showTitle);
        return fileTokens.Count > 0 &&
               BestMatch(fileName, fileTokens, showTitle, specialsEpisodes.Append(episode).ToList()) is { } best &&
               best.SeasonNumber == episode.SeasonNumber &&
               best.EpisodeNumber == episode.EpisodeNumber;
    }

    // The single best entry over the threshold whose numbers the file name carries; null when none, or when the
    // best score is shared (SPEC-03).
    private static TrackedEpisode? BestMatch(
        string fileName,
        IReadOnlySet<string> fileTokens,
        string? showTitle,
        IReadOnlyList<TrackedEpisode> specialsEpisodes)
    {
        var fileNumbers = FileNumbers(fileName);
        var scored = specialsEpisodes
            .Where(episode => episode.SeasonNumber == AppConstants.SpecialsSeasonNumber)
            .GroupBy(episode => episode.EpisodeNumber)
            .Select(group => group.First())
            .Where(episode => TitleNumbers(episode.Title).IsSubsetOf(fileNumbers))
            .Select(episode => (Episode: episode, Score: Score(fileTokens, EpisodeTitleTokens(episode.Title, showTitle))))
            .Where(item => item.Score.Share >= MinimumShare)
            .OrderByDescending(item => item.Score.Share)
            .ThenByDescending(item => item.Score.Overlap)
            .ToList();
        if (scored.Count == 0 || (scored.Count > 1 && scored[1].Score == scored[0].Score))
        {
            return null;
        }

        return scored[0].Episode;
    }

    private static HashSet<int> FileNumbers(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName.Replace('\\', '/').Split('/').Last());
        return StandaloneNumbers(BracketGroupRegex.Replace(stem, " "));
    }

    private static HashSet<int> TitleNumbers(string? title) => StandaloneNumbers(title ?? string.Empty);

    private static HashSet<int> StandaloneNumbers(string text) =>
        Words(text)
            .Where(token => token.Length <= 4 && token.All(char.IsDigit))
            .Select(int.Parse)
            .ToHashSet();

    public static IReadOnlySet<string> FileTitleTokens(string fileName, string? showTitle)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName.Replace('\\', '/').Split('/').Last());
        return Meaningful(BracketGroupRegex.Replace(stem, " "), showTitle).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlySet<string> EpisodeTitleTokens(string? title, string? showTitle) =>
        Meaningful(title ?? string.Empty, showTitle).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static (double Share, int Overlap) Score(IReadOnlySet<string> fileTokens, IReadOnlySet<string> episodeTokens)
    {
        if (episodeTokens.Count == 0)
        {
            return (0, 0);
        }

        var overlap = episodeTokens.Count(fileTokens.Contains);
        return ((double)overlap / episodeTokens.Count, overlap);
    }

    private static IEnumerable<string> Meaningful(string text, string? showTitle)
    {
        var showTokens = Words(showTitle ?? string.Empty).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Words(text).Where(token =>
            token.Length > 1 &&
            !token.Any(char.IsDigit) &&
            !StopWords.Contains(token) &&
            !NoiseWords.Contains(token) &&
            !showTokens.Contains(token));
    }

    private static IEnumerable<string> Words(string text)
    {
        var buffer = new char[text.Length];
        for (var index = 0; index < text.Length; index++)
        {
            buffer[index] = char.IsLetterOrDigit(text[index]) || text[index] == '\'' ? char.ToLowerInvariant(text[index]) : ' ';
        }

        return new string(buffer)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.EndsWith("'s", StringComparison.Ordinal) ? token[..^2] : token.Replace("'", string.Empty));
    }

    private static int? ResolveSpecialIndex(TorrentCandidateParseResult parsed)
    {
        if (parsed.PreferEpisodeIndexMatch)
        {
            return parsed.ReleaseSpecialIndex ?? parsed.EpisodeNumber;
        }

        if (parsed.IsSpecialContent && parsed.ReleaseSpecialIndex is > 0)
        {
            return parsed.ReleaseSpecialIndex;
        }

        return null;
    }

    private static bool IsClaimed(int episodeNumber, IReadOnlySet<int>? claimedEpisodeNumbers) =>
        claimedEpisodeNumbers is not null && claimedEpisodeNumbers.Contains(episodeNumber);
}
