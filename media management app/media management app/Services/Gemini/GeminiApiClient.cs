using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services.Gemini;

public sealed class GeminiApiClient : IGeminiApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ISettingsService _settingsService;
    private readonly HttpClient _httpClient;
    private readonly GeminiQuotaTracker _quotaTracker;
    private readonly IGeminiModelCatalogService _modelCatalog;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _requestGate = new(1, 1);

    public GeminiApiClient(
        ISettingsService settingsService,
        HttpClient httpClient,
        GeminiQuotaTracker quotaTracker,
        IGeminiModelCatalogService modelCatalog,
        IAppLogger logger)
    {
        _settingsService = settingsService;
        _httpClient = httpClient;
        _quotaTracker = quotaTracker;
        _modelCatalog = modelCatalog;
        _logger = logger;
    }

    public async Task<string> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var result = await GenerateWithFallbackAsync(
            systemPrompt: "Reply with JSON {\"ok\":true}.",
            userJson: "{\"ping\":true}",
            recordQuota: false,
            responseSchema: null,
            progress: null,
            cancellationToken);
        _logger.Info($"Gemini connection test succeeded (model: {result.Model}).", LogTarget.All);
        return result.Model;
    }

    public async Task<string> GenerateJsonTextAsync(
        string systemPrompt,
        string userJson,
        CancellationToken cancellationToken = default)
    {
        var result = await GenerateWithFallbackAsync(
            systemPrompt,
            userJson,
            recordQuota: true,
            responseSchema: null,
            progress: null,
            cancellationToken);
        return result.Json;
    }

    public async Task<GeminiJsonTextResult> GenerateJsonTextWithModelAsync(
        string systemPrompt,
        string userJson,
        JsonElement? responseSchema = null,
        IProgress<PackLinkProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = await GenerateWithFallbackAsync(
            systemPrompt,
            userJson,
            recordQuota: true,
            responseSchema,
            progress,
            cancellationToken);
        return new GeminiJsonTextResult(result.Model, result.Json);
    }

    public async Task<T> GenerateJsonAsync<T>(
        string systemPrompt,
        string userJson,
        CancellationToken cancellationToken = default)
    {
        var result = await GenerateWithFallbackAsync(
            systemPrompt,
            userJson,
            recordQuota: true,
            responseSchema: null,
            progress: null,
            cancellationToken);
        var parsed = JsonSerializer.Deserialize<T>(result.Json, SerializerOptions);
        if (parsed is null)
        {
            throw new InvalidOperationException("Gemini returned JSON that could not be parsed.");
        }

        return parsed;
    }

    private async Task<GeminiGenerateResult> GenerateWithFallbackAsync(
        string systemPrompt,
        string userJson,
        bool recordQuota,
        JsonElement? responseSchema,
        IProgress<PackLinkProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var models = GetModelChain();
        Exception? lastError = null;

        for (var modelIndex = 0; modelIndex < models.Count; modelIndex++)
        {
            var model = models[modelIndex];
            if (modelIndex > 0)
            {
                ReportWaiting(progress, $"Switching to fallback: {model}");
                _logger.Info($"Gemini trying fallback model '{model}'.", LogTarget.All);
            }

            for (var attempt = 0; attempt <= AppConstants.GeminiMaxRetriesPerModel; attempt++)
            {
                try
                {
                    var json = await SendGenerateRequestAsync(
                        model,
                        systemPrompt,
                        userJson,
                        recordQuota,
                        responseSchema,
                        cancellationToken);

                    if (string.IsNullOrWhiteSpace(json))
                    {
                        throw new InvalidOperationException($"Gemini model '{model}' returned an empty response.");
                    }

                    if (recordQuota)
                    {
                        _logger.Info(
                            $"Gemini generateContent succeeded (model: {model}, responseChars: {json.Length}).",
                            LogTarget.All);
                    }

                    return new GeminiGenerateResult(model, json);
                }
                catch (GeminiRequestFailedException ex)
                {
                    lastError = ex;

                    if (ex.StatusCode is HttpStatusCode.BadRequest)
                    {
                        _logger.Warning(
                            $"Gemini model '{model}' returned HTTP 400 (unsupported for text JSON). Skipping model.",
                            LogTarget.All);
                        break;
                    }

                    if (ex.StatusCode is HttpStatusCode.NotFound)
                    {
                        _logger.Warning(
                            $"Gemini model '{model}' returned HTTP 404. Trying next model.",
                            LogTarget.All);
                        break;
                    }

                    if (IsBackoffStatus(ex.StatusCode) && attempt < AppConstants.GeminiMaxRetriesPerModel)
                    {
                        var delayMs = CalculateBackoffDelayMs(attempt, ex.RetryAfterSeconds);
                        var retryNumber = attempt + 1;
                        var statusLabel = FormatStatusLabel(ex.StatusCode);
                        var message =
                            $"{model}: {statusLabel}, retry {retryNumber}/{AppConstants.GeminiMaxRetriesPerModel} in {delayMs / 1000}s…";
                        _logger.Warning(message, LogTarget.All);
                        ReportWaiting(progress, message);
                        await Task.Delay(delayMs, cancellationToken);
                        continue;
                    }

                    if (IsBackoffStatus(ex.StatusCode))
                    {
                        _logger.Warning(
                            $"Gemini model '{model}' exhausted retries after HTTP {(int)ex.StatusCode}. Trying next model.",
                            LogTarget.All);
                        break;
                    }

                    _logger.Warning($"Gemini model '{model}' failed: {ex.Message}", LogTarget.All);
                    break;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    _logger.Warning($"Gemini model '{model}' failed: {ex.Message}", LogTarget.All);
                    break;
                }
            }
        }

        throw new InvalidOperationException(
            BuildFallbackFailureMessage(models, lastError),
            lastError);
    }

    private async Task<string?> SendGenerateRequestAsync(
        string model,
        string systemPrompt,
        string userJson,
        bool recordQuota,
        JsonElement? responseSchema,
        CancellationToken cancellationToken)
    {
        var apiKey = _settingsService.Current.Gemini?.ApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Gemini API key is not configured.");
        }

        if (recordQuota)
        {
            _quotaTracker.EnsureCanRequest();
        }

        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            await _quotaTracker.WaitForSpacingAsync(cancellationToken);

            var timeoutSeconds = Math.Clamp(
                _settingsService.Current.Gemini?.TimeoutSeconds ?? 45,
                10,
                120);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            var requestBody = new GeminiGenerateContentRequest
            {
                SystemInstruction = new GeminiContent
                {
                    Parts = [new GeminiPart { Text = systemPrompt }]
                },
                Contents =
                [
                    new GeminiContent
                    {
                        Parts = [new GeminiPart { Text = userJson }]
                    }
                ],
                GenerationConfig = new GeminiGenerationConfig
                {
                    ResponseMimeType = "application/json",
                    ResponseSchema = responseSchema
                }
            };

            var url = $"{AppConstants.GeminiApiBaseUrl}models/{model}:generateContent?key={Uri.EscapeDataString(apiKey)}";
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(requestBody, SerializerOptions),
                    Encoding.UTF8,
                    "application/json")
            };

            using var response = await _httpClient.SendAsync(request, timeoutCts.Token);
            var responseText = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                var retryAfterSeconds = TryReadRetryAfterSeconds(response);
                throw new GeminiRequestFailedException(
                    $"Gemini returned HTTP {(int)response.StatusCode} for model '{model}': {TrimForLog(responseText)}",
                    response.StatusCode,
                    retryAfterSeconds);
            }

            var parsed = JsonSerializer.Deserialize<GeminiGenerateContentResponse>(responseText, SerializerOptions);
            var text = parsed?.Candidates?
                .SelectMany(candidate => candidate.Content?.Parts ?? [])
                .Select(part => part.Text)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

            if (recordQuota)
            {
                _quotaTracker.RecordRequest();
            }

            return text?.Trim();
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private string GetPrimaryModel() =>
        _modelCatalog.Normalize(_settingsService.Current.Gemini?.Model);

    private IReadOnlyList<string> GetModelChain()
    {
        var chain = new List<string> { GetPrimaryModel() };
        var fallbacks = _settingsService.Current.Gemini?.FallbackModels ?? [];
        if (fallbacks.Length == 0)
        {
            fallbacks = _modelCatalog.FallbackModels.ToArray();
        }

        foreach (var fallback in fallbacks)
        {
            if (chain.Count >= AppConstants.GeminiMaxFallbackModels + 1)
            {
                break;
            }

            var normalized = _modelCatalog.Normalize(fallback);
            if (!chain.Contains(normalized, StringComparer.OrdinalIgnoreCase)
                && GeminiTextModelFilter.IsSuitableForTextJsonMapping(normalized))
            {
                chain.Add(normalized);
            }
        }

        return chain;
    }

    private static bool IsBackoffStatus(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.TooManyRequests
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.BadGateway;

    private static int CalculateBackoffDelayMs(int attempt, int? retryAfterSeconds)
    {
        var exponentialMs = AppConstants.GeminiRetryBaseDelayMs * (int)Math.Pow(2, attempt);
        if (retryAfterSeconds is > 0)
        {
            exponentialMs = Math.Max(exponentialMs, retryAfterSeconds.Value * 1000);
        }

        return Math.Clamp(exponentialMs, AppConstants.GeminiRetryBaseDelayMs, 60_000);
    }

    private static int? TryReadRetryAfterSeconds(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Retry-After", out var values))
        {
            return null;
        }

        var value = values.FirstOrDefault();
        if (int.TryParse(value, out var seconds))
        {
            return seconds;
        }

        return null;
    }

    private static string FormatStatusLabel(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.TooManyRequests => "rate limited (429)",
        HttpStatusCode.ServiceUnavailable => "high demand (503)",
        HttpStatusCode.BadGateway => "bad gateway (502)",
        _ => $"HTTP {(int)statusCode}"
    };

    private static void ReportWaiting(IProgress<PackLinkProgressUpdate>? progress, string message)
    {
        progress?.Report(new PackLinkProgressUpdate
        {
            Step = PackLinkProgressStep.WaitingForGemini,
            Status = PackLinkProgressStatus.Active,
            Message = message
        });
    }

    private static string BuildFallbackFailureMessage(IReadOnlyList<string> models, Exception? lastError)
    {
        var modelList = string.Join(", ", models);
        var detail = lastError?.Message ?? "Unknown error.";
        return $"Gemini API request failed for all configured models ({modelList}). {detail}";
    }

    private static string TrimForLog(string value) =>
        value.Length <= 240 ? value : value[..240] + "...";

    private sealed record GeminiGenerateResult(string Model, string Json);

    private sealed class GeminiRequestFailedException : Exception
    {
        public GeminiRequestFailedException(string message, HttpStatusCode statusCode, int? retryAfterSeconds)
            : base(message)
        {
            StatusCode = statusCode;
            RetryAfterSeconds = retryAfterSeconds;
        }

        public HttpStatusCode StatusCode { get; }

        public int? RetryAfterSeconds { get; }
    }

    private sealed class GeminiGenerateContentRequest
    {
        public GeminiContent? SystemInstruction { get; set; }

        public List<GeminiContent> Contents { get; set; } = [];

        public GeminiGenerationConfig? GenerationConfig { get; set; }
    }

    private sealed class GeminiGenerationConfig
    {
        public string? ResponseMimeType { get; set; }

        public JsonElement? ResponseSchema { get; set; }
    }

    private sealed class GeminiContent
    {
        public List<GeminiPart> Parts { get; set; } = [];
    }

    private sealed class GeminiPart
    {
        public string? Text { get; set; }
    }

    private sealed class GeminiGenerateContentResponse
    {
        public List<GeminiCandidate>? Candidates { get; set; }
    }

    private sealed class GeminiCandidate
    {
        public GeminiContent? Content { get; set; }
    }
}
