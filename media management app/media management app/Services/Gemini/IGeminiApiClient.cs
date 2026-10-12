using System.Text.Json;
using media_management_app.Models;

namespace media_management_app.Services.Gemini;

public interface IGeminiApiClient
{
    Task<string> TestConnectionAsync(CancellationToken cancellationToken = default);

    Task<string> GenerateJsonTextAsync(
        string systemPrompt,
        string userJson,
        CancellationToken cancellationToken = default);

    Task<GeminiJsonTextResult> GenerateJsonTextWithModelAsync(
        string systemPrompt,
        string userJson,
        JsonElement? responseSchema = null,
        IProgress<PackLinkProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);

    Task<T> GenerateJsonAsync<T>(
        string systemPrompt,
        string userJson,
        CancellationToken cancellationToken = default);
}
