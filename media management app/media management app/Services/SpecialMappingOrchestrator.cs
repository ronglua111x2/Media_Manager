using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services.Gemini;

namespace media_management_app.Services;

public interface ISpecialMappingOrchestrator
{
    Task<SpecialMappingResult> ResolveAsync(
        IReadOnlyList<(string RelativePath, string FileName)> files,
        PackFolderTreeAnalysis tree,
        IReadOnlyList<TrackedEpisode> specialsEpisodes,
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
        IReadOnlyList<TrackedEpisode> specialsEpisodes,
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

        var request = PackSpecialPayloadBuilder.Build(files, tree, specialsEpisodes, showTitle, tmdbId);
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
            var offlineResult = PackSpecialPayloadBuilder.ApplyProposals(
                request,
                specialsEpisodes,
                SpecialMappingSource.Rule);
            SpecialMappingValidator.Validate(offlineResult, specialsEpisodes);
            return offlineResult;
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
            return cached;
        }

        var aiResult = await _geminiSpecialMappingProvider.FinalizeAsync(
            request,
            specialsEpisodes,
            progress,
            cancellationToken);
        SpecialMappingValidator.Validate(aiResult, specialsEpisodes);
        _cache.Save(torrentHash, tmdbId, aiResult);
        return aiResult;
    }
}
