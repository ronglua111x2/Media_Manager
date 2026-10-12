using System.Text.RegularExpressions;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public static class SpecialEpisodeTitleMatcher
{
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

        var titleMatch = MatchByTitleTokens(parsed, candidates);
        if (titleMatch is not null && !IsClaimed(titleMatch.EpisodeNumber, claimedEpisodeNumbers))
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

    private static TrackedEpisode? MatchByTitleTokens(TorrentCandidateParseResult parsed, IReadOnlyList<TrackedEpisode> candidates)
    {
        var searchTokens = BuildSearchTokens(parsed);
        if (searchTokens.Count == 0)
        {
            return null;
        }

        TrackedEpisode? bestMatch = null;
        var bestScore = 0;
        foreach (var candidate in candidates)
        {
            var episodeTokens = TorrentCandidateParser.Tokenize(candidate.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (episodeTokens.Count == 0)
            {
                continue;
            }

            var overlap = searchTokens.Count(token => episodeTokens.Contains(token));
            if (overlap > bestScore)
            {
                bestScore = overlap;
                bestMatch = candidate;
            }
        }

        return bestScore >= 2 ? bestMatch : null;
    }

    private static HashSet<string> BuildSearchTokens(TorrentCandidateParseResult parsed)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in parsed.TitleTokens)
        {
            tokens.Add(token);
        }

        if (!string.IsNullOrWhiteSpace(parsed.EpisodeTitle))
        {
            foreach (var token in TorrentCandidateParser.Tokenize(parsed.EpisodeTitle))
            {
                tokens.Add(token);
            }
        }

        foreach (var token in TorrentCandidateParser.Tokenize(parsed.RawTitle))
        {
            if (token.Length >= 4 &&
                !IsNoiseToken(token))
            {
                tokens.Add(token);
            }
        }

        return tokens;
    }

    private static bool IsNoiseToken(string token)
    {
        return token.Equals("ova", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("special", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("mkv", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("mp4", StringComparison.OrdinalIgnoreCase) ||
               Regex.IsMatch(token, @"^s\d+e\d+$", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(token, @"^s\d+s\d+$", RegexOptions.IgnoreCase);
    }
}
