using System.Text.RegularExpressions;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class SnapshotCandidateMatcher
{
    private static readonly Regex SeasonWordRegex = new(@"\bseason\s*(?<season>\d{1,3})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public SnapshotMatchResult Match(
        TrackedShow show,
        TrackedEpisode episode,
        SnapshotCandidate candidate,
        IReadOnlyList<string> selectedQualities,
        IReadOnlyList<string> titleVariants,
        CandidateScoringWeights weights,
        IReadOnlyList<string>? preferTerms = null)
    {
        var result = candidate.Result;
        var parsed = candidate.Parsed;

        if (!result.CanAdd)
        {
            return new SnapshotMatchResult { IsAccepted = false, RejectReason = $"not addable link type '{result.LinkType}'" };
        }

        if (LooksLikePluginError(result.FileName))
        {
            return new SnapshotMatchResult { IsAccepted = false, RejectReason = "search plugin error row" };
        }

        if (parsed.ExplicitYear is not null && show.FirstAirYear is not null && parsed.ExplicitYear != show.FirstAirYear)
        {
            return new SnapshotMatchResult
            {
                IsAccepted = false,
                RejectReason = $"explicit year mismatch {parsed.ExplicitYear} != {show.FirstAirYear}"
            };
        }

        var isAbsoluteEpisodeMatch = parsed.AbsoluteEpisodeNumber is not null && parsed.SeasonNumber is null;
        if (!isAbsoluteEpisodeMatch && !IsSeasonMatch(parsed, episode.SeasonNumber))
        {
            return new SnapshotMatchResult
            {
                IsAccepted = false,
                RejectReason = $"season mismatch {episode.SeasonNumber:00}"
            };
        }

        if (result.Seeders < show.MinimumSeeders)
        {
            return new SnapshotMatchResult
            {
                IsAccepted = false,
                RejectReason = $"seeders below threshold {show.MinimumSeeders}"
            };
        }

        if (!TorrentQuality.MatchesSelectedQuality(parsed.Quality, selectedQualities))
        {
            return new SnapshotMatchResult
            {
                IsAccepted = false,
                RejectReason = $"does not match selected quality options: {string.Join(", ", selectedQualities)}"
            };
        }

        var titleMatch = EvaluateTitleMatch(titleVariants, parsed.TitleTokens);
        if (!titleMatch.IsMatch)
        {
            return new SnapshotMatchResult { IsAccepted = false, RejectReason = "does not contain enough show title or alias tokens" };
        }

        if (isAbsoluteEpisodeMatch)
        {
            if (parsed.AbsoluteEpisodeNumber != episode.EpisodeNumber)
            {
                return new SnapshotMatchResult
                {
                    IsAccepted = false,
                    RejectReason = $"does not contain absolute episode {episode.EpisodeNumber}"
                };
            }
        }
        else if (parsed.SeasonNumber != episode.SeasonNumber || parsed.EpisodeNumber != episode.EpisodeNumber)
        {
            return new SnapshotMatchResult
            {
                IsAccepted = false,
                RejectReason = $"does not contain S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00} or {episode.SeasonNumber}x{episode.EpisodeNumber:00}"
            };
        }

        var episodeScore = EvaluateEpisodeTitleMatch(episode.Title, parsed.EpisodeTitle, out var episodeRejectReason);
        if (episodeRejectReason is not null)
        {
            return new SnapshotMatchResult { IsAccepted = false, RejectReason = episodeRejectReason };
        }

        var qualityScore = TorrentQuality.GetRank(parsed.Quality);
        var audioScore = PreferredTermMatcher.CountMatches(result.FileName, show.PreferredAudioCodec);
        var preferTermsScore = PreferredTermMatcher.CountMatches(result.FileName, preferTerms);
        var identityScore = titleMatch.Score + (parsed.ExplicitYear is not null && parsed.ExplicitYear == show.FirstAirYear ? 10 : 0);
        var sizeScore = TorrentQuality.CalculateSizeScore(result.FileSize, weights: weights);
        var totalScore = TorrentQuality.CalculateCandidateScore(
            qualityScore,
            audioScore,
            result.Seeders,
            identityScore,
            episodeScore,
            weights,
            preferTermsScore,
            sizeScore);

        return new SnapshotMatchResult
        {
            IsAccepted = true,
            IdentityScore = identityScore,
            EpisodeScore = episodeScore,
            QualityScore = qualityScore,
            AudioScore = audioScore,
            PreferTermsScore = preferTermsScore,
            SizeScore = sizeScore,
            TotalScore = totalScore
        };
    }

    private static bool LooksLikePluginError(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return true;
        }

        return fileName.Contains("api key error", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("right-click this row", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("open description", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("jackett:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSeasonMatch(TorrentCandidateParseResult parsed, int seasonNumber)
    {
        if (parsed.SeasonNumber is not null)
        {
            return parsed.SeasonNumber == seasonNumber;
        }

        if (parsed.RawTitle.Contains($"S{seasonNumber:00}", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (parsed.RawTitle.Contains($"{seasonNumber}x", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var match = SeasonWordRegex.Match(parsed.RawTitle);
        return match.Success && int.TryParse(match.Groups["season"].Value, out var parsedSeason) && parsedSeason == seasonNumber;
    }

    private static (bool IsMatch, int Score) EvaluateTitleMatch(
        IReadOnlyList<string> titleVariants,
        IReadOnlyList<string> candidateTokens)
    {
        foreach (var showTitle in titleVariants.Where(title => !string.IsNullOrWhiteSpace(title)))
        {
            var showTokens = TorrentCandidateParser.Tokenize(showTitle)
                .Where(token => token.Length > 2)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (showTokens.Count == 0)
            {
                return (true, 1);
            }

            var matched = showTokens.Count(token => candidateTokens.Contains(token, StringComparer.OrdinalIgnoreCase));
            var required = Math.Max(2, (int)Math.Ceiling(showTokens.Count * 0.6));
            if (matched >= required)
            {
                return (true, matched);
            }
        }

        return (false, 0);
    }

    private static int EvaluateEpisodeTitleMatch(string? targetTitle, string? candidateTitle, out string? rejectReason)
    {
        rejectReason = null;
        if (string.IsNullOrWhiteSpace(targetTitle) || string.IsNullOrWhiteSpace(candidateTitle))
        {
            return 0;
        }

        var targetTokens = TorrentCandidateParser.Tokenize(targetTitle)
            .Where(token => token.Length > 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var candidateTokens = TorrentCandidateParser.Tokenize(candidateTitle)
            .Where(token => token.Length > 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (targetTokens.Count < 2 || candidateTokens.Count < 2)
        {
            return 0;
        }

        var overlap = targetTokens.Count(token => candidateTokens.Contains(token, StringComparer.OrdinalIgnoreCase));
        if (overlap == 0)
        {
            rejectReason = "episode title mismatch";
            return 0;
        }

        return overlap;
    }
}
