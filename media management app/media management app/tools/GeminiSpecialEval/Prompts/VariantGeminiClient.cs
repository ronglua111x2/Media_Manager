using System.Text.Json;
using media_management_app.Models;
using media_management_app.Services.Gemini;

namespace GeminiSpecialEval.Prompts;

/// <summary>
/// Sits between the app's <c>GeminiSpecialMappingProvider</c> and the app's <see cref="GeminiApiClient"/>:
/// swaps in the chosen prompt variant and remembers exactly what was sent and what came back.
/// </summary>
internal sealed class VariantGeminiClient : IGeminiApiClient
{
    private readonly IGeminiApiClient _inner;
    private readonly PromptVariant _variant;

    public VariantGeminiClient(IGeminiApiClient inner, PromptVariant variant)
    {
        _inner = inner;
        _variant = variant;
    }

    public int Calls { get; private set; }

    public string? LivePrompt { get; private set; }

    public string? LivePayload { get; private set; }

    public string? SentPrompt { get; private set; }

    public string? SentPayload { get; private set; }

    public string? ResponseText { get; private set; }

    public string? AnsweredBy { get; private set; }

    public string? FinishReason { get; private set; }

    public string? Error { get; private set; }

    public async Task<GeminiJsonTextResult> GenerateJsonTextWithModelAsync(
        string systemPrompt,
        string userJson,
        JsonElement? responseSchema = null,
        IProgress<PackLinkProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        LivePrompt = systemPrompt;
        LivePayload = userJson;
        SentPrompt = _variant.RewritePrompt(systemPrompt);
        SentPayload = _variant.RewritePayload(userJson);
        try
        {
            var result = await _inner.GenerateJsonTextWithModelAsync(SentPrompt, SentPayload, responseSchema, progress, cancellationToken);
            ResponseText = result.Json;
            AnsweredBy = result.Model;
            FinishReason = result.FinishReason;
            return result;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            throw;
        }
    }

    public Task<string> TestConnectionAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The eval only sends special-mapping requests.");

    public Task<string> GenerateJsonTextAsync(string systemPrompt, string userJson, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The eval only sends special-mapping requests.");

    public Task<T> GenerateJsonAsync<T>(string systemPrompt, string userJson, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The eval only sends special-mapping requests.");
}
