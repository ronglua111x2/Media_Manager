using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services.Gemini;

namespace media_management_app.Services;

public interface ISpecialMappingOrchestrator
{
    /// <param name="episodes">
    /// Every tracked episode of the show. Season 0 is what specials map to; the air dates of the regular seasons
    /// split it into per-season windows for the deterministic tier.
    /// </param>
    Task<SpecialMappingResult> ResolveAsync(
        IReadOnlyList<(string RelativePath, string FileName)> files,
        PackFolderTreeAnalysis tree,
        IReadOnlyList<TrackedEpisode> episodes,
        string showTitle,
        int tmdbId,
        string torrentHash,
        IProgress<PackLinkProgressUpdate>? progress = null,
        bool useGemini = true,
        bool bypassCache = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a Gemini result the user accepted (PR-15: nothing is cached before that). Does nothing for rule-only
    /// results or results that must not be cached.
    /// </summary>
    void RememberAccepted(SpecialMappingResult result);
}

public sealed class SpecialMappingOrchestrator : ISpecialMappingOrchestrator
{
    private readonly ISettingsService _settingsService;
    private readonly GeminiSpecialMappingProvider _geminiSpecialMappingProvider;
    private readonly SpecialMappingCache _cache;
    private readonly IAppLogger _logger;

    public SpecialMappingOrchestrator(
        ISettingsService settingsService,
        GeminiSpecialMappingProvider geminiSpecialMappingProvider,
        SpecialMappingCache cache,
        IAppLogger logger)
    {
        _settingsService = settingsService;
        _geminiSpecialMappingProvider = geminiSpecialMappingProvider;
        _cache = cache;
        _logger = logger;
    }

    public async Task<SpecialMappingResult> ResolveAsync(
        IReadOnlyList<(string RelativePath, string FileName)> files,
        PackFolderTreeAnalysis tree,
        IReadOnlyList<TrackedEpisode> episodes,
        string showTitle,
        int tmdbId,
        string torrentHash,
        IProgress<PackLinkProgressUpdate>? progress = null,
        bool useGemini = true,
        bool bypassCache = false,
        CancellationToken cancellationToken = default)
    {
        PackLinkProgressReporter.Report(
            progress,
            PackLinkProgressStep.BuildingPayload,
            PackLinkProgressStatus.Active,
            "Scanning pack for special/OVA candidates...");

        var (specialsEpisodes, regularEpisodes) = SplitEpisodes(episodes);
        var request = PackSpecialPayloadBuilder.Build(files, tree, specialsEpisodes, showTitle, tmdbId, regularEpisodes);
        if (request.Context.CandidateCount == 0)
        {
            PackLinkProgressReporter.Report(
                progress,
                PackLinkProgressStep.BuildingPayload,
                PackLinkProgressStatus.Done,
                "No special/OVA candidates found in pack.");
            return new SpecialMappingResult();
        }

        _logger.Info(
            $"Special mapping payload: candidates={request.Context.CandidateCount}, proposals={request.Proposals.Count}, tmdbS00={request.Context.TmdbSpecialCount}, mismatch={request.Context.CountMismatch}.",
            LogTarget.All);

        PackLinkProgressReporter.Report(
            progress,
            PackLinkProgressStep.BuildingPayload,
            PackLinkProgressStatus.Done,
            $"{request.Context.CandidateCount} special/OVA candidates, {request.Context.TmdbSpecialCount} TMDB S00 episodes, {request.Proposals.Count} rule proposals.");

        var gemini = _settingsService.Current.Gemini ?? new GeminiSettings();
        if (!useGemini || !gemini.Enabled || string.IsNullOrWhiteSpace(gemini.ApiKey))
        {
            return ApplyRules(request, specialsEpisodes, fallbackReason: null);
        }

        if (request.GeminiCandidateIndices.Count == 0)
        {
            // The rules placed every special: a pack like this costs no Gemini request (PR-14).
            _logger.Info("Special mapping: the rules placed every special/OVA file; Gemini not called.", LogTarget.All);
            PackLinkProgressReporter.Report(
                progress,
                PackLinkProgressStep.WaitingForGemini,
                PackLinkProgressStatus.Done,
                "Skipped Gemini (rules placed every special).");
            return ApplyRules(request, specialsEpisodes, fallbackReason: null);
        }

        var shortHash = torrentHash[..Math.Min(8, torrentHash.Length)];
        var cacheKey = SpecialMappingCache.BuildKey(
            torrentHash,
            tmdbId,
            specialsEpisodes,
            request.Context.Candidates,
            GeminiSpecialMappingProvider.SpecialPromptVersion);

        if (bypassCache)
        {
            _logger.Info(
                $"Gemini special mapping cache bypassed for torrent={shortHash} tmdbId={tmdbId}.",
                LogTarget.All);
        }

        if (!bypassCache && _cache.TryGet(cacheKey, torrentHash, tmdbId, out var cached, out var isLegacy) && cached is not null)
        {
            var fromCache = Rehydrate(cached.Result, specialsEpisodes);
            var problems = CheckCached(fromCache, request, specialsEpisodes, isLegacy);
            if (problems.Count == 0)
            {
                _logger.Info(
                    $"Gemini special mapping cache hit for torrent={shortHash} tmdbId={tmdbId}{(isLegacy ? " (entry from before PR-15, checked against the rules)" : string.Empty)}.",
                    LogTarget.All);
                PackLinkProgressReporter.Report(
                    progress,
                    PackLinkProgressStep.WaitingForGemini,
                    PackLinkProgressStatus.Done,
                    "Skipped API call (cached mapping).");
                PackLinkProgressReporter.Report(
                    progress,
                    PackLinkProgressStep.ParsingResponse,
                    PackLinkProgressStatus.Done,
                    $"Using cached mapping ({fromCache.Items.Count(item => item.Episode is not null)} mapped specials).");
                return fromCache.WithCacheInfo(cacheKey, cached.Model, fromCache: true);
            }

            _logger.Warning(
                $"Cached special mapping for torrent={shortHash} tmdbId={tmdbId} no longer passes the checks ({problems[0]}); asking Gemini again.",
                LogTarget.All);
        }

        SpecialMappingResult aiResult;
        try
        {
            aiResult = await _geminiSpecialMappingProvider.FinalizeAsync(
                request,
                specialsEpisodes,
                progress,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(
                $"Gemini special mapping failed; using rule mappings with review: {ex.Message}",
                LogTarget.All);
            PackLinkProgressReporter.Report(
                progress,
                PackLinkProgressStep.ParsingResponse,
                PackLinkProgressStatus.Done,
                "Gemini failed. Using rule mappings; review required.",
                ex.Message);
            return ApplyRules(request, specialsEpisodes, $"Gemini special mapping failed: {ex.Message}");
        }

        var checkedResult = SpecialMappingValidator.RemoveViolations(aiResult, specialsEpisodes, request);
        if (checkedResult.RequiresReview)
        {
            _logger.Warning(
                $"Gemini special mapping had {checkedResult.Warnings.Count} warning(s); conflicting pairs were dropped and the result will not be cached.",
                LogTarget.All);
            return checkedResult.WithCacheInfo(null, aiResult.GeminiModel);
        }

        // PR-15: cached only when the user accepts the review (RememberAccepted).
        return checkedResult.WithCacheInfo(cacheKey, aiResult.GeminiModel);
    }

    public void RememberAccepted(SpecialMappingResult result)
    {
        if (!result.UsedGemini || string.IsNullOrWhiteSpace(result.CacheKey))
        {
            return;
        }

        var parts = result.CacheKey.Split('|');
        if (parts.Length < 3 || !int.TryParse(parts[2], out var tmdbId))
        {
            return;
        }

        _cache.Save(result.CacheKey, parts[1], tmdbId, result, result.GeminiModel);
        _logger.Info(
            $"Accepted special mapping cached for torrent={parts[1][..Math.Min(8, parts[1].Length)]} tmdbId={tmdbId} (model {result.GeminiModel ?? "unknown"}).",
            LogTarget.All);
    }

    /// <summary>Points the cached items at the show's current TMDB episodes (the cache keeps only a slim copy).</summary>
    private static SpecialMappingResult Rehydrate(SpecialMappingResult cached, IReadOnlyList<TrackedEpisode> specialsEpisodes)
    {
        var byNumber = specialsEpisodes
            .GroupBy(episode => episode.EpisodeNumber)
            .ToDictionary(group => group.Key, group => group.First());
        TrackedEpisode? Live(TrackedEpisode? episode) =>
            episode is null ? null : byNumber.GetValueOrDefault(episode.EpisodeNumber) ?? episode;

        var items = cached.Items
            .Select(item => new SpecialMappingItem
            {
                RelativePath = item.RelativePath,
                Episode = Live(item.Episode),
                Source = item.Source,
                ProposalReason = item.ProposalReason,
                Reason = item.Reason,
                TitleConfirmed = item.TitleConfirmed,
                SuggestedEpisode = Live(item.SuggestedEpisode),
                MatchesSuggestion = item.MatchesSuggestion
            })
            .ToList();
        return new SpecialMappingResult
        {
            Items = items,
            Warnings = cached.Warnings,
            UsedGemini = cached.UsedGemini,
            ByPath = items
                .GroupBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase)
        };
    }

    /// <summary>
    /// A cached result is used only if it still passes every hard check. An entry from before PR-15 must also
    /// cover the same files and agree with every pair the rules now place.
    /// </summary>
    private static IReadOnlyList<string> CheckCached(
        SpecialMappingResult cached,
        SpecialMappingAIRequest request,
        IReadOnlyList<TrackedEpisode> specialsEpisodes,
        bool isLegacy)
    {
        var problems = SpecialMappingValidator.Validate(cached, specialsEpisodes)
            .Concat(SpecialMappingValidator.ValidateGeminiPairs(cached, request))
            .Select(violation => violation.Message)
            .ToList();
        if (!isLegacy)
        {
            return problems;
        }

        var candidatePaths = request.Context.Candidates
            .Select(candidate => candidate.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!candidatePaths.SetEquals(cached.Items.Select(item => item.RelativePath)))
        {
            problems.Add("the pack's special files changed");
        }

        foreach (var proposal in request.Proposals)
        {
            var candidate = request.Context.Candidates[proposal.CandidateIndex];
            var item = cached.ByPath.GetValueOrDefault(candidate.RelativePath);
            if (item?.Episode?.EpisodeNumber != proposal.ProposedS00E)
            {
                problems.Add($"'{candidate.FileName}' is S00E{proposal.ProposedS00E:00} by the rules but S00E{item?.Episode?.EpisodeNumber:00} in the cache");
            }
        }

        return problems;
    }

    /// <summary>
    /// The offline rule path on its own, for callers whose special step failed before or outside
    /// <see cref="ResolveAsync"/>. Never calls Gemini and never reads or writes the cache.
    /// </summary>
    public static SpecialMappingResult ResolveOffline(
        IReadOnlyList<(string RelativePath, string FileName)> files,
        PackFolderTreeAnalysis tree,
        IReadOnlyList<TrackedEpisode> episodes,
        string showTitle,
        int tmdbId,
        string? fallbackReason)
    {
        var (specialsEpisodes, regularEpisodes) = SplitEpisodes(episodes);
        var request = PackSpecialPayloadBuilder.Build(files, tree, specialsEpisodes, showTitle, tmdbId, regularEpisodes);
        if (request.Context.CandidateCount == 0)
        {
            var empty = new SpecialMappingResult();
            return fallbackReason is null ? empty : empty.WithReview([FallbackWarning(fallbackReason)], fallbackReason);
        }

        return ApplyRules(request, specialsEpisodes, fallbackReason);
    }

    private static SpecialMappingResult ApplyRules(
        SpecialMappingAIRequest request,
        IReadOnlyList<TrackedEpisode> specialsEpisodes,
        string? fallbackReason)
    {
        var result = SpecialMappingValidator.RemoveViolations(
            PackSpecialPayloadBuilder.ApplyProposals(request, specialsEpisodes, SpecialMappingSource.Rule),
            specialsEpisodes);
        return fallbackReason is null
            ? result
            : result.WithReview([FallbackWarning(fallbackReason)], fallbackReason);
    }

    private static (IReadOnlyList<TrackedEpisode> Specials, IReadOnlyList<TrackedEpisode> Regular) SplitEpisodes(
        IReadOnlyList<TrackedEpisode> episodes) =>
        (episodes.Where(episode => episode.SeasonNumber == AppConstants.SpecialsSeasonNumber).ToList(),
         episodes.Where(episode => episode.SeasonNumber > 0).ToList());

    private static string FallbackWarning(string fallbackReason) =>
        $"{fallbackReason.TrimEnd('.')}. Special/OVA files use rule mappings instead; check them before linking.";
}
