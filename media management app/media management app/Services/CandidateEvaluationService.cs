using media_management_app.Models;

namespace media_management_app.Services;

public sealed class CandidateEvaluationService : ICandidateEvaluationService
{
    public RecipeCandidateResult EvaluateEpisode(SearchRecipe recipe, TrackedShow show, TrackedEpisode episode, TorrentSearchResult result)
    {
        var parsed = TorrentCandidateParser.Parse(result.FileName);
        var filter = GetModule(recipe, RecipeBlockType.CandidateFilter);
        var identity = GetModule(recipe, RecipeBlockType.Identity);
        var reject = GetCommonRejectReason(recipe, result, parsed, filter);
        if (reject.Reason != CandidateRejectReason.None)
        {
            return Rejected(result, reject.Reason, reject.Detail);
        }

        if (parsed.SeasonNumber != episode.SeasonNumber || parsed.EpisodeNumber != episode.EpisodeNumber)
        {
            return Rejected(result, CandidateRejectReason.EpisodeMismatch, $"does not contain S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}");
        }

        if (parsed.ExplicitYear is not null && show.FirstAirYear is not null && parsed.ExplicitYear != show.FirstAirYear)
        {
            return Rejected(result, CandidateRejectReason.YearMismatch, $"explicit year mismatch {parsed.ExplicitYear} != {show.FirstAirYear}");
        }

        var titleMatch = EvaluateTitleMatch(show.Title, identity, parsed.TitleTokens);
        if (!titleMatch.IsMatch)
        {
            return Rejected(result, CandidateRejectReason.TitleMismatch, "does not contain enough show title or alias tokens");
        }

        var episodeScore = EvaluateEpisodeTitleMatch(episode.Title, parsed.EpisodeTitle);
        var audioScore = GetAudioScore(filter, result.FileName);
        var qualityScore = TorrentQuality.GetRank(parsed.Quality);
        return Accepted(result, qualityScore, titleMatch.Score, episodeScore, audioScore);
    }

    public RecipeCandidateResult EvaluateMovie(SearchRecipe recipe, TrackedMovie movie, TorrentSearchResult result)
    {
        var parsed = TorrentCandidateParser.Parse(result.FileName);
        var filter = GetModule(recipe, RecipeBlockType.CandidateFilter);
        var identity = GetModule(recipe, RecipeBlockType.Identity);
        var reject = GetCommonRejectReason(recipe, result, parsed, filter);
        if (reject.Reason != CandidateRejectReason.None)
        {
            return Rejected(result, reject.Reason, reject.Detail);
        }

        if (movie.ReleaseYear is not null && !result.FileName.Contains(movie.ReleaseYear.Value.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return Rejected(result, CandidateRejectReason.YearMismatch, $"does not contain release year {movie.ReleaseYear}");
        }

        var titleMatch = EvaluateTitleMatch(movie.Title, identity, parsed.TitleTokens.Count > 0 ? parsed.TitleTokens : TorrentCandidateParser.Tokenize(result.FileName).ToList());
        if (!titleMatch.IsMatch)
        {
            return Rejected(result, CandidateRejectReason.TitleMismatch, "does not contain enough movie title or alias tokens");
        }

        var audioScore = GetAudioScore(filter, result.FileName);
        var qualityScore = TorrentQuality.GetRank(TorrentQuality.Detect(result.FileName));
        return Accepted(result, qualityScore, titleMatch.Score, episodeScore: 0, audioScore);
    }

    private static (CandidateRejectReason Reason, string Detail) GetCommonRejectReason(
        SearchRecipe recipe,
        TorrentSearchResult result,
        TorrentCandidateParseResult parsed,
        RecipeModuleConfig? filter)
    {
        if (!result.CanAdd)
        {
            return (CandidateRejectReason.NotAddable, $"not addable link type '{result.LinkType}'");
        }

        if (LooksLikePluginError(result.FileName))
        {
            return (CandidateRejectReason.PluginError, "search plugin error row");
        }

        if (filter is null)
        {
            return (CandidateRejectReason.None, string.Empty);
        }

        if (result.Seeders < filter.MinimumSeeders)
        {
            return (CandidateRejectReason.SeedersTooLow, $"seeders below threshold {filter.MinimumSeeders}");
        }

        if (filter.MaximumSizeBytes is not null && result.FileSize > filter.MaximumSizeBytes.Value)
        {
            return (CandidateRejectReason.SizeTooLarge, "candidate is larger than recipe maximum size");
        }

        var detectedQuality = string.IsNullOrWhiteSpace(parsed.Quality) ? TorrentQuality.Detect(result.FileName) : parsed.Quality;
        if (!TorrentQuality.MatchesSelectedQuality(detectedQuality, filter.QualityAllowList))
        {
            return (CandidateRejectReason.QualityMismatch, $"does not match selected quality options: {string.Join(", ", filter.QualityAllowList)}");
        }

        var missingTerm = filter.IncludeTerms
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .FirstOrDefault(term => !result.FileName.Contains(term, StringComparison.OrdinalIgnoreCase));
        if (missingTerm is not null)
        {
            return (CandidateRejectReason.MissingIncludeTerm, $"missing include term '{missingTerm}'");
        }

        var excludedTerm = filter.ExcludeTerms
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .FirstOrDefault(term => result.FileName.Contains(term, StringComparison.OrdinalIgnoreCase));
        if (excludedTerm is not null)
        {
            return (CandidateRejectReason.ExcludedTerm, $"contains excluded term '{excludedTerm}'");
        }

        var blockedGroup = filter.BlockedReleaseGroups
            .Where(group => !string.IsNullOrWhiteSpace(group))
            .FirstOrDefault(group => result.FileName.Contains(group, StringComparison.OrdinalIgnoreCase));
        if (blockedGroup is not null)
        {
            return (CandidateRejectReason.BlockedReleaseGroup, $"contains blocked group '{blockedGroup}'");
        }

        return (CandidateRejectReason.None, string.Empty);
    }

    private static RecipeModuleConfig? GetModule(SearchRecipe recipe, RecipeBlockType blockType)
    {
        return recipe.Modules.FirstOrDefault(module => module.BlockType == blockType && module.IsEnabled);
    }

    private static RecipeCandidateResult Accepted(
        TorrentSearchResult result,
        int qualityScore,
        int identityScore,
        int episodeScore,
        int audioScore)
    {
        return new RecipeCandidateResult
        {
            SearchResult = result,
            QualityScore = qualityScore,
            IdentityScore = identityScore,
            EpisodeScore = episodeScore,
            AudioScore = audioScore,
            TotalScore = TorrentQuality.CalculateCandidateScore(qualityScore, audioScore, result.Seeders, identityScore, episodeScore)
        };
    }

    private static RecipeCandidateResult Rejected(TorrentSearchResult result, CandidateRejectReason reason, string detail)
    {
        return new RecipeCandidateResult
        {
            SearchResult = result,
            RejectReason = reason,
            RejectDetail = detail
        };
    }

    private static bool LooksLikePluginError(string fileName)
    {
        return string.IsNullOrWhiteSpace(fileName) ||
               fileName.Contains("api key error", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("right-click this row", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("open description", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("jackett:", StringComparison.OrdinalIgnoreCase);
    }

    private static (bool IsMatch, int Score) EvaluateTitleMatch(
        string title,
        RecipeModuleConfig? identity,
        IReadOnlyList<string> candidateTokens)
    {
        var titles = new List<string> { title };
        if (identity is not null)
        {
            titles.AddRange(identity.Aliases.Where(alias => !string.IsNullOrWhiteSpace(alias)));
        }

        foreach (var candidateTitle in titles)
        {
            var titleTokens = TorrentCandidateParser.Tokenize(candidateTitle)
                .Where(token => token.Length > 2)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (titleTokens.Count == 0)
            {
                return (true, 1);
            }

            var matched = titleTokens.Count(token => candidateTokens.Contains(token, StringComparer.OrdinalIgnoreCase));
            var required = Math.Max(1, Math.Min(2, titleTokens.Count));
            if (matched >= required)
            {
                return (true, matched);
            }
        }

        return (false, 0);
    }

    private static int EvaluateEpisodeTitleMatch(string? targetTitle, string? candidateTitle)
    {
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
        return targetTokens.Count(token => candidateTokens.Contains(token, StringComparer.OrdinalIgnoreCase));
    }

    private static int GetAudioScore(RecipeModuleConfig? filter, string fileName)
    {
        return filter is not null &&
               !string.IsNullOrWhiteSpace(filter.PreferredAudioCodec) &&
               fileName.Contains(filter.PreferredAudioCodec, StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;
    }
}
