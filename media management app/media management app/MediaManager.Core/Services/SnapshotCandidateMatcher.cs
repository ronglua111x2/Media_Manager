using System.Text.RegularExpressions;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class SnapshotCandidateMatcher
{
    private static readonly Regex SeasonWordRegex = new(@"\bseason\s*(?<season>\d{1,3})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public SnapshotMatchResult Match(
        TrackedShow show,
        TrackedEpisode episode,
        SnapshotCandidate candidate,
        SearchRecipe recipe,
        IReadOnlyList<string> titleVariants,
        CandidateScoringWeights weights)
    {
        var result = candidate.Result;
        var parsed = candidate.Parsed;

        if (!result.CanAdd)
        {
            return Reject(CandidateRejectReason.NotAddable, $"not addable link type '{result.LinkType}'");
        }

        if (RecipeCandidateFilter.LooksLikePluginError(result.FileName))
        {
            return Reject(CandidateRejectReason.PluginError, "search plugin error row");
        }

        var kind = TorrentReleaseKind.Classify(result.FileName, parsed);
        var kindReject = TorrentReleaseKind.GetRejectReasonForTarget(MediaKind.TvEpisode, kind);
        if (kindReject is not null)
        {
            return Reject(CandidateRejectReason.WrongReleaseKind, kindReject);
        }

        if (parsed.ExplicitYear is not null && show.FirstAirYear is not null && parsed.ExplicitYear != show.FirstAirYear)
        {
            return Reject(
                CandidateRejectReason.YearMismatch,
                $"explicit year mismatch {parsed.ExplicitYear} != {show.FirstAirYear}");
        }

        var isAbsoluteEpisodeMatch = parsed.AbsoluteEpisodeNumber is not null && parsed.SeasonNumber is null;
        if (!isAbsoluteEpisodeMatch && !IsSeasonMatch(parsed, episode.SeasonNumber))
        {
            return Reject(CandidateRejectReason.EpisodeMismatch, $"season mismatch {episode.SeasonNumber:00}");
        }

        var titleMatch = EvaluateTitleMatch(titleVariants, parsed.TitleTokens);
        if (!titleMatch.IsMatch)
        {
            return Reject(CandidateRejectReason.TitleMismatch, "does not contain enough show title or alias tokens");
        }

        if (isAbsoluteEpisodeMatch)
        {
            if (parsed.AbsoluteEpisodeNumber != episode.EpisodeNumber)
            {
                return Reject(
                    CandidateRejectReason.EpisodeMismatch,
                    $"does not contain absolute episode {episode.EpisodeNumber}");
            }
        }
        else if (parsed.SeasonNumber != episode.SeasonNumber || parsed.EpisodeNumber != episode.EpisodeNumber)
        {
            return Reject(
                CandidateRejectReason.EpisodeMismatch,
                $"does not contain S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00} or {episode.SeasonNumber}x{episode.EpisodeNumber:00}");
        }

        var episodeScore = EvaluateEpisodeTitleMatch(episode.Title, parsed.EpisodeTitle, out var episodeRejectReason);
        if (episodeRejectReason is not null)
        {
            return Reject(CandidateRejectReason.EpisodeMismatch, episodeRejectReason);
        }

        var filterReject = RecipeCandidateFilter.GetRejectReason(recipe, result, parsed);
        if (filterReject.Reason != CandidateRejectReason.None)
        {
            return Reject(filterReject.Reason, filterReject.Detail);
        }

        var filter = RecipeCandidateFilter.GetFilterModule(recipe);
        var qualityScore = TorrentQuality.GetRank(parsed.Quality);
        var audioScore = PreferredTermMatcher.CountMatches(result.FileName, filter?.PreferredAudioCodec);
        var preferTermsScore = PreferredTermMatcher.CountMatches(result.FileName, filter?.PreferTerms);
        var identityScore = titleMatch.Score + (parsed.ExplicitYear is not null && parsed.ExplicitYear == show.FirstAirYear ? 10 : 0);
        var sizeScore = TorrentQualityScoring.CalculateSizeScore(
            result.FileSize,
            filter?.MinimumSizeBytes,
            filter?.MaximumSizeBytes,
            weights);
        var engineRankScore = RecipeRuntimeSettings.ResolveEngineRankScore(result.EngineName, filter);
        var totalScore = TorrentQualityScoring.CalculateCandidateScore(
            qualityScore,
            audioScore,
            result.Seeders,
            identityScore,
            episodeScore,
            weights,
            preferTermsScore,
            sizeScore,
            engineRankScore);

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

    private static SnapshotMatchResult Reject(CandidateRejectReason reason, string detail)
    {
        return new SnapshotMatchResult
        {
            IsAccepted = false,
            RejectReason = detail,
            RejectReasonCode = reason,
            RejectDetail = detail
        };
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
