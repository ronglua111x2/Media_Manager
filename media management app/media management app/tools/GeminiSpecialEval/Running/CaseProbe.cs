using GeminiSpecialEval.Cases;
using GeminiSpecialEval.Infrastructure;
using GeminiSpecialEval.Prompts;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;
using media_management_app.Services.Gemini;

namespace GeminiSpecialEval.Running;

/// <summary>What the app's builder makes of a case, with zero requests: rule pairs, the files Gemini would get, the payload.</summary>
internal sealed record CaseProbeResult(
    EvalCase Case,
    SpecialMappingAIRequest Request,
    IReadOnlyList<TrackedEpisode> Specials,
    string? LivePayload)
{
    public bool CallsGemini => Request.GeminiCandidateIndices.Count > 0;

    public string PayloadFor(PromptVariant variant) =>
        LivePayload is null ? string.Empty : variant.RewritePayload(LivePayload);
}

internal static class CaseProbe
{
    private static string? _livePrompt;

    /// <summary>Builds the request exactly as <see cref="SpecialMappingOrchestrator"/> does.</summary>
    public static CaseProbeResult Probe(EvalCase evalCase)
    {
        var specials = evalCase.Episodes.Where(episode => episode.SeasonNumber == AppConstants.SpecialsSeasonNumber).ToList();
        var regular = evalCase.Episodes.Where(episode => episode.SeasonNumber > 0).ToList();
        var tree = PackFolderTreeAnalyzer.Analyze(evalCase.Files.Select(file => file.RelativePath).ToList());
        var request = PackSpecialPayloadBuilder.Build(evalCase.Files, tree, specials, evalCase.ShowTitle, evalCase.TmdbId, regular);
        var payload = request.GeminiCandidateIndices.Count > 0 ? SpecialMappingCompactJson.SerializeRequest(request) : null;
        return new CaseProbeResult(evalCase, request, specials, payload);
    }

    /// <summary>The system prompt the app's provider sends today, captured without a request.</summary>
    public static string LivePrompt()
    {
        if (_livePrompt is not null)
        {
            return _livePrompt;
        }

        var capture = new CapturingClient();
        var settings = new EvalSettingsService();
        settings.Current.Gemini = new GeminiSettings { Enabled = true, ApiKey = "capture" };
        new GeminiSpecialMappingProvider(capture, settings, new EvalLogger())
            .FinalizeAsync(new SpecialMappingAIRequest(), [])
            .GetAwaiter()
            .GetResult();
        return _livePrompt = capture.Prompt ?? throw new InvalidOperationException("The provider sent no prompt.");
    }

    public static string PromptSha(PromptVariant variant) => Json.Sha256(variant.RewritePrompt(LivePrompt()));

    private sealed class CapturingClient : IGeminiApiClient
    {
        public string? Prompt { get; private set; }

        public Task<GeminiJsonTextResult> GenerateJsonTextWithModelAsync(
            string systemPrompt,
            string userJson,
            System.Text.Json.JsonElement? responseSchema = null,
            IProgress<PackLinkProgressUpdate>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Prompt = systemPrompt;
            return Task.FromResult(new GeminiJsonTextResult("capture", "{\"m\":[],\"w\":[]}", "STOP"));
        }

        public Task<string> TestConnectionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string> GenerateJsonTextAsync(string systemPrompt, string userJson, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<T> GenerateJsonAsync<T>(string systemPrompt, string userJson, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
