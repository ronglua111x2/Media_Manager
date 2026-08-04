using media_management_app.Models;

namespace media_management_app.Services;

public sealed class CandidateMatchResult
{
    public bool IsAccepted { get; init; }

    public string? RejectReason { get; init; }

    public int IdentityScore { get; init; }

    public int EpisodeScore { get; init; }

    public int QualityScore { get; init; }

    public int AudioScore { get; init; }

    public int PreferTermsScore { get; init; }

    public int SizeScore { get; init; }

    public int TotalScore { get; init; }
}

public static class CandidateMatcher
{
    public static CandidateMatchResult MatchEpisodeCandidate(
        TrackedShow show,
        TrackedEpisode episode,
        TorrentSearchResult result,
        IReadOnlyList<string> selectedQualities,
        CandidateScoringWeights weights,
        IReadOnlyList<string>? preferTerms = null)
    {
        if (!result.CanAdd)
        {
            return new CandidateMatchResult { IsAccepted = false, RejectReason = $"not addable link type '{result.LinkType}'" };
        }

        if (LooksLikePluginError(result.FileName))
        {
            return new CandidateMatchResult { IsAccepted = false, RejectReason = "search plugin error row" };
        }

        var parsed = TorrentCandidateParser.Parse(result.FileName);
        if (!IsEpisodeMatch(parsed, episode))
        {
            return new CandidateMatchResult
            {
                IsAccepted = false,
                RejectReason = $"does not contain S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00} or {episode.SeasonNumber}x{episode.EpisodeNumber:00}"
            };
        }

        if (result.Seeders < show.MinimumSeeders)
        {
            return new CandidateMatchResult
            {
                IsAccepted = false,
                RejectReason = $"seeders below threshold {show.MinimumSeeders}"
            };
        }

        if (!TorrentQuality.MatchesSelectedQuality(parsed.Quality, selectedQualities))
        {
            return new CandidateMatchResult
            {
                IsAccepted = false,
                RejectReason = $"does not match selected quality options: {string.Join(", ", selectedQualities)}"
            };
        }

        if (parsed.ExplicitYear is not null && show.FirstAirYear is not null && parsed.ExplicitYear != show.FirstAirYear)
        {
            return new CandidateMatchResult
            {
                IsAccepted = false,
                RejectReason = $"explicit year mismatch {parsed.ExplicitYear} != {show.FirstAirYear}"
            };
        }

        var titleMatch = EvaluateTitleMatch(show.Title, parsed.TitleTokens);
        if (!titleMatch.IsMatch)
        {
            return new CandidateMatchResult { IsAccepted = false, RejectReason = "does not contain enough show title tokens" };
        }

        var episodeScore = EvaluateEpisodeTitleMatch(episode.Title, parsed.EpisodeTitle, out var episodeRejectReason);
        if (episodeRejectReason is not null)
        {
            return new CandidateMatchResult { IsAccepted = false, RejectReason = episodeRejectReason };
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

        return new CandidateMatchResult
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

    private static bool IsEpisodeMatch(TorrentCandidateParseResult parsed, TrackedEpisode episode)
    {
        return parsed.SeasonNumber == episode.SeasonNumber && parsed.EpisodeNumber == episode.EpisodeNumber;
    }

    private static (bool IsMatch, int Score) EvaluateTitleMatch(string showTitle, IReadOnlyList<string> candidateTokens)
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
        if (matched < required)
        {
            return (false, 0);
        }

        return (true, matched);
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
