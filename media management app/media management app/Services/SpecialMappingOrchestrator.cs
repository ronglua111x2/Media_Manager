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

        if (bypassCache)
        {
            _logger.Info(
                $"Gemini special mapping cache bypassed for torrent={torrentHash[..Math.Min(8, torrentHash.Length)]} tmdbId={tmdbId}.",
                LogTarget.All);
        }

        if (!bypassCache && _cache.TryGet(torrentHash, tmdbId, out var cached) && cached is not null)
        {
            _logger.Info(
                $"Gemini special mapping cache hit for torrent={torrentHash[..Math.Min(8, torrentHash.Length)]} tmdbId={tmdbId}.",
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
                $"Using cached mapping ({cached.Items.Count(item => item.Episode is not null)} mapped specials).");
            return SpecialMappingValidator.RemoveViolations(cached, specialsEpisodes);
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

        var checkedResult = SpecialMappingValidator.RemoveViolations(aiResult, specialsEpisodes);
        if (checkedResult.RequiresReview)
        {
            _logger.Warning(
                $"Gemini special mapping had {checkedResult.Warnings.Count} warning(s); conflicting pairs were dropped and the result is not cached.",
                LogTarget.All);
            return checkedResult;
        }

        _cache.Save(torrentHash, tmdbId, checkedResult);
        return checkedResult;
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
