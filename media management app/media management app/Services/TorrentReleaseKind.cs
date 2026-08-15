using System.Text.RegularExpressions;
using media_management_app.Common;

namespace media_management_app.Services;

public sealed class TorrentReleaseKindFlags
{
    public bool IsEpisode { get; init; }

    public bool IsPack { get; init; }

    public bool IsMovie { get; init; }

    public bool IsUnknown => !IsEpisode && !IsPack && !IsMovie;
}

/// <summary>
/// Multi-label release-kind classifier for hard-rejecting results that do not match a recipe target.
/// A torrent may be pack and movie at once; an explicit single-episode marker makes it episode-only.
/// </summary>
public static class TorrentReleaseKind
{
    private static readonly Regex ExplicitEpisodeRegex = new(
        @"\bS\d{1,3}E\d{1,4}\b|\b\d{1,3}x\d{1,4}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PackKeywordRegex = new(
        @"\b(?:complete|batch|pack)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SeasonWordRegex = new(
        @"\bSeasons?\s*\d{1,3}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SeasonRangeRegex = new(
        @"\bS\d{1,3}\s*(?:-|to)\s*S?\d{1,3}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EpisodeRangeRegex = new(
        @"\((?<from>\d{1,3})\s*(?:-|to|~|～|〜)\s*(?<to>\d{1,3})\)|\b(?<from>\d{1,3})\s*(?:-|to|~|～|〜)\s*(?<to>\d{1,3})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MovieKeywordRegex = new(
        @"\b(?:movies?|films?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BareSeasonTokenRegex = new(
        @"\bS\d{1,3}\b(?!\s*E\d)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AbsoluteEpTokenRegex = new(
        @"\bEP\s*\d{1,4}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex YearTokenRegex = new(
        @"\b(?:19|20)\d{2}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static TorrentReleaseKindFlags Classify(string fileName, TorrentCandidateParseResult? parsed = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return new TorrentReleaseKindFlags();
        }

        parsed ??= TorrentCandidateParser.Parse(fileName);

        if (HasExplicitSingleEpisodeMarker(fileName, parsed))
        {
            return new TorrentReleaseKindFlags { IsEpisode = true };
        }

        var isPack = HasPackSignals(fileName, parsed);
        var hasMovieKeyword = MovieKeywordRegex.IsMatch(fileName);

        if (isPack)
        {
            return new TorrentReleaseKindFlags
            {
                IsPack = true,
                IsMovie = hasMovieKeyword
            };
        }

        if (hasMovieKeyword || LooksLikeStandaloneMovie(fileName, parsed))
        {
            return new TorrentReleaseKindFlags { IsMovie = true };
        }

        return new TorrentReleaseKindFlags();
    }

    public static string? GetRejectReasonForTarget(MediaKind targetKind, TorrentReleaseKindFlags kind)
    {
        return targetKind switch
        {
            MediaKind.TvEpisode => GetEpisodeRejectReason(kind),
            MediaKind.TvSeasonPack => GetPackRejectReason(kind),
            MediaKind.Movie => GetMovieRejectReason(kind),
            _ => null
        };
    }

    public static bool HasExplicitSingleEpisodeMarker(string fileName, TorrentCandidateParseResult? parsed = null)
    {
        if (ExplicitEpisodeRegex.IsMatch(fileName))
        {
            return true;
        }

        parsed ??= TorrentCandidateParser.Parse(fileName);
        if (parsed.SeasonNumber is not null && parsed.EpisodeNumber is not null)
        {
            return true;
        }

        // Valid anime-absolute episode (parser already skips Season NN / bare SNN / years).
        return parsed.AbsoluteEpisodeNumber is not null && parsed.SeasonNumber is null;
    }

    public static bool HasPackSignals(string fileName, TorrentCandidateParseResult? parsed = null)
    {
        if (HasExplicitSingleEpisodeMarker(fileName, parsed))
        {
            return false;
        }

        if (HasPackSignalsFromTitle(fileName))
        {
            return true;
        }

        if (parsed is null)
        {
            return false;
        }

        return parsed.CoveredSeasons.Count >= 1 &&
               parsed.EpisodeNumber is null &&
               parsed.AbsoluteEpisodeNumber is null;
    }

    /// <summary>
    /// Pack signals detectable from the title alone (safe to call from inside the parser).
    /// </summary>
    public static bool HasPackSignalsFromTitle(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || ExplicitEpisodeRegex.IsMatch(fileName))
        {
            return false;
        }

        return PackKeywordRegex.IsMatch(fileName) ||
               SeasonWordRegex.IsMatch(fileName) ||
               SeasonRangeRegex.IsMatch(fileName) ||
               HasEpisodeRange(fileName) ||
               BareSeasonTokenRegex.IsMatch(fileName);
    }

    public static bool HasMovieKeyword(string fileName) =>
        !string.IsNullOrWhiteSpace(fileName) && MovieKeywordRegex.IsMatch(fileName);

    /// <summary>
    /// Digits that must not be treated as anime-absolute episode numbers.
    /// </summary>
    public static bool IsExcludedAbsoluteEpisodeDigitMatch(string normalizedTitle, Capture digitCapture)
    {
        if (digitCapture.Length is < 2 or > 4 || !int.TryParse(digitCapture.Value, out var value))
        {
            return true;
        }

        // Years.
        if (value is >= 1900 and <= 2099)
        {
            return true;
        }

        var index = digitCapture.Index;
        var before = index > 0 ? normalizedTitle[..index] : string.Empty;

        // Digits that are the NN in "Season NN" / "Seasons NN".
        if (Regex.IsMatch(before, @"\bSeasons?\s*$", RegexOptions.IgnoreCase))
        {
            return true;
        }

        // Bare SNN not followed by E (e.g. S01 in "S01 COMPLETE").
        if (Regex.IsMatch(before, @"\bS$", RegexOptions.IgnoreCase))
        {
            var after = index + digitCapture.Length < normalizedTitle.Length
                ? normalizedTitle[(index + digitCapture.Length)..]
                : string.Empty;
            if (!Regex.IsMatch(after, @"^\s*E\d", RegexOptions.IgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetEpisodeRejectReason(TorrentReleaseKindFlags kind)
    {
        if (kind.IsEpisode)
        {
            return null;
        }

        if (kind.IsPack && kind.IsMovie)
        {
            return "wrong release kind for TV episode (season pack + movie)";
        }

        if (kind.IsPack)
        {
            return "wrong release kind for TV episode (season pack)";
        }

        if (kind.IsMovie)
        {
            return "wrong release kind for TV episode (movie)";
        }

        return null;
    }

    private static string? GetPackRejectReason(TorrentReleaseKindFlags kind)
    {
        if (kind.IsPack)
        {
            return null;
        }

        if (kind.IsEpisode)
        {
            return "wrong release kind for TV pack (single episode)";
        }

        if (kind.IsMovie)
        {
            return "wrong release kind for TV pack (movie-only)";
        }

        return null;
    }

    private static string? GetMovieRejectReason(TorrentReleaseKindFlags kind)
    {
        if (kind.IsMovie)
        {
            return null;
        }

        if (kind.IsEpisode)
        {
            return "wrong release kind for movie (single episode)";
        }

        if (kind.IsPack)
        {
            return "wrong release kind for movie (season pack)";
        }

        return null;
    }

    private static bool HasEpisodeRange(string fileName)
    {
        foreach (Match match in EpisodeRangeRegex.Matches(fileName))
        {
            if (!int.TryParse(match.Groups["from"].Value, out var from) ||
                !int.TryParse(match.Groups["to"].Value, out var to))
            {
                continue;
            }

            // Avoid treating year ranges (2020-2021) as episode ranges.
            if (from is >= 1900 and <= 2099 || to is >= 1900 and <= 2099)
            {
                continue;
            }

            if (from != to && from is >= 1 and <= 999 && to is >= 1 and <= 999)
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeStandaloneMovie(string fileName, TorrentCandidateParseResult parsed)
    {
        // Typical movie title: no episode marker, no season coverage, often has a year.
        if (parsed.CoveredSeasons.Count > 0 ||
            parsed.SeasonNumber is not null ||
            parsed.EpisodeNumber is not null ||
            parsed.AbsoluteEpisodeNumber is not null ||
            BareSeasonTokenRegex.IsMatch(fileName) ||
            AbsoluteEpTokenRegex.IsMatch(fileName) ||
            SeasonWordRegex.IsMatch(fileName))
        {
            return false;
        }

        return parsed.ExplicitYear is not null || YearTokenRegex.IsMatch(fileName);
    }
}
