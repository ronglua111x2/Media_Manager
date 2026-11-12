using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services.Gemini.Routing;

namespace media_management_app.Services.Gemini;

/// <summary>Gemini refused the request in a way no other model can fix (invalid key, verification, billing).</summary>
public sealed class GeminiRequestBlockedException : InvalidOperationException
{
    public GeminiRequestBlockedException(string message, HttpStatusCode statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

public sealed class GeminiApiClient : IGeminiApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ISettingsService _settingsService;
    private readonly HttpClient _httpClient;
    private readonly GeminiModelRouter _router;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _requestGate = new(1, 1);

    public GeminiApiClient(
        ISettingsService settingsService,
        HttpClient httpClient,
        GeminiModelRouter router,
        IAppLogger logger)
    {
        _settingsService = settingsService;
        _httpClient = httpClient;
        _router = router;
        _logger = logger;
    }

    public async Task<string> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var result = await GenerateRoutedAsync(
            systemPrompt: "Reply with JSON {\"ok\":true}.",
            userJson: "{\"ping\":true}",
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
        var result = await GenerateRoutedAsync(systemPrompt, userJson, responseSchema: null, progress: null, cancellationToken);
        return result.Json;
    }

    public async Task<GeminiJsonTextResult> GenerateJsonTextWithModelAsync(
        string systemPrompt,
        string userJson,
        JsonElement? responseSchema = null,
        IProgress<PackLinkProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = await GenerateRoutedAsync(systemPrompt, userJson, responseSchema, progress, cancellationToken);
        return new GeminiJsonTextResult(result.Model, result.Json, result.FinishReason);
    }

    public async Task<T> GenerateJsonAsync<T>(
        string systemPrompt,
        string userJson,
        CancellationToken cancellationToken = default)
    {
        var result = await GenerateRoutedAsync(systemPrompt, userJson, responseSchema: null, progress: null, cancellationToken);
        var parsed = JsonSerializer.Deserialize<T>(result.Json, SerializerOptions);
        if (parsed is null)
        {
            throw new InvalidOperationException("Gemini returned JSON that could not be parsed.");
        }

        return parsed;
    }

    private async Task<GeminiGenerateResult> GenerateRoutedAsync(
        string systemPrompt,
        string userJson,
        JsonElement? responseSchema,
        IProgress<PackLinkProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var apiKey = _settingsService.Current.Gemini?.ApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Gemini API key is not configured.");
        }

        var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();

        while (tried.Count < AppConstants.GeminiMaxModelsPerCall)
        {
            var decision = _router.Select(tried);
            if (decision.Selected is not { } route)
            {
                var reason = decision.UnavailableReason ?? "No Gemini model is available.";
                if (decision.NextAvailableUtc is { } next)
                {
                    reason += $" Next model frees up at {next.ToLocalTime():yyyy-MM-dd HH:mm}.";
                }

                if (failures.Count > 0)
                {
                    reason += " Tried: " + string.Join("; ", failures);
                }

                throw new InvalidOperationException(reason);
            }

            tried.Add(route.Model);
            if (tried.Count > 1)
            {
                ReportWaiting(progress, $"Switching to {route.Model}");
                _logger.Info($"Gemini trying next model '{route.Model}'.", LogTarget.All);
            }

            if (route.Wait > TimeSpan.Zero)
            {
                ReportWaiting(progress, $"{route.Model}: waiting {Math.Ceiling(route.Wait.TotalSeconds)}s for the per-minute limit…");
                await Task.Delay(route.Wait, cancellationToken);
            }

            var serverRetryUsed = false;
            while (true)
            {
                try
                {
                    var (json, finishReason) = await SendGenerateRequestAsync(
                        apiKey,
                        route,
                        systemPrompt,
                        userJson,
                        responseSchema,
                        cancellationToken);

                    if (string.IsNullOrWhiteSpace(json))
                    {
                        throw new InvalidOperationException(
                            $"Gemini model '{route.Model}' returned an empty response{FormatFinishReason(finishReason)}.");
                    }

                    _logger.Info(
                        $"Gemini generateContent succeeded (model: {route.Model}, responseChars: {json.Length}).",
                        LogTarget.All);

                    if (IsEarlyStop(finishReason))
                    {
                        _logger.Warning(
                            $"Gemini model '{route.Model}' stopped early (finishReason={finishReason}); the answer may be incomplete.",
                            LogTarget.All);
                    }

                    return new GeminiGenerateResult(route.Model, json, finishReason);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (GeminiHttpFailureException failure)
                {
                    var outcome = GeminiErrorClassifier.Classify(failure.StatusCode, failure.ResponseBody);
                    if (_router.Report(route.Model, outcome))
                    {
                        _logger.Warning($"Gemini request stopped (HTTP {(int)failure.StatusCode}): {outcome.Message}", LogTarget.All);
                        throw new GeminiRequestBlockedException(outcome.Message, failure.StatusCode);
                    }

                    if (outcome.Kind == GeminiOutcomeKind.ServerError && !serverRetryUsed)
                    {
                        serverRetryUsed = true;
                        var delay = TimeSpan.FromMilliseconds(2000 + Random.Shared.Next(0, 2000));
                        var message = $"{route.Model}: HTTP {(int)failure.StatusCode}, retrying once in {delay.TotalSeconds:0}s…";
                        _logger.Warning(message, LogTarget.All);
                        ReportWaiting(progress, message);
                        await Task.Delay(delay, cancellationToken);
                        continue;
                    }

                    var label = $"{route.Model}: {DescribeOutcome(outcome, failure.StatusCode)}";
                    failures.Add(label);
                    _logger.Warning($"Gemini {label}. Trying another model.", LogTarget.All);
                    ReportWaiting(progress, label);
                    break;
                }
                catch (OperationCanceledException)
                {
                    // Our own timeout, not the caller's cancellation: treat like an overloaded model.
                    _router.Report(route.Model, new GeminiCallOutcome(GeminiOutcomeKind.Overloaded, "Request timed out."));
                    var label = $"{route.Model}: timed out after {GetTimeoutSeconds()}s";
                    failures.Add(label);
                    _logger.Warning($"Gemini {label}. Trying another model.", LogTarget.All);
                    ReportWaiting(progress, label);
                    break;
                }
                catch (Exception ex) when (ex is not GeminiRequestBlockedException)
                {
                    failures.Add($"{route.Model}: {ex.Message}");
                    _logger.Warning($"Gemini model '{route.Model}' failed: {ex.Message}", LogTarget.All);
                    break;
                }
            }
        }

        throw new InvalidOperationException(
            $"Gemini API request failed for {tried.Count} model(s). {string.Join("; ", failures)}");
    }

    private async Task<(string? Text, string? FinishReason)> SendGenerateRequestAsync(
        string apiKey,
        GeminiRoutedModel route,
        string systemPrompt,
        string userJson,
        JsonElement? responseSchema,
        CancellationToken cancellationToken)
    {
        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(GetTimeoutSeconds()));

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
                    ResponseSchema = responseSchema,
                    ThinkingConfig = BuildThinkingConfig(route)
                }
            };

            var url = $"{AppConstants.GeminiApiBaseUrl}models/{Uri.EscapeDataString(route.Model)}:generateContent";
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(requestBody, SerializerOptions),
                    Encoding.UTF8,
                    "application/json")
            };
            request.Headers.Add("x-goog-api-key", apiKey);

            // Google counts every request against the quota, including the ones that fail.
            _router.RecordAttempt(route.Model);

            using var response = await _httpClient.SendAsync(request, timeoutCts.Token);
            var responseText = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new GeminiHttpFailureException(response.StatusCode, responseText);
            }

            var parsed = JsonSerializer.Deserialize<GeminiGenerateContentResponse>(responseText, SerializerOptions);
            var answer = parsed?.Candidates?
                .Select(candidate => (
                    Text: candidate.Content?.Parts?
                        .Where(part => part.Thought != true)
                        .Select(part => part.Text)
                        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
                    candidate.FinishReason))
                .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate.Text));
            var finishReason = answer?.FinishReason ?? parsed?.Candidates?.FirstOrDefault()?.FinishReason;

            if (!string.IsNullOrWhiteSpace(parsed?.ModelVersion) &&
                !string.Equals(parsed.ModelVersion, route.Model, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Warning(
                    $"Gemini answered '{route.Model}' with model '{parsed.ModelVersion}' (Google redirected the model).",
                    LogTarget.All);
            }

            return (answer?.Text?.Trim(), finishReason);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private int GetTimeoutSeconds() =>
        Math.Clamp(
            _settingsService.Current.Gemini?.TimeoutSeconds ?? AppConstants.GeminiDefaultTimeoutSeconds,
            10,
            180);

    private static GeminiThinkingConfig? BuildThinkingConfig(GeminiRoutedModel route)
    {
        if (!string.IsNullOrWhiteSpace(route.ThinkingLevel))
        {
            return new GeminiThinkingConfig { ThinkingLevel = route.ThinkingLevel };
        }

        return route.ThinkingBudget is { } budget ? new GeminiThinkingConfig { ThinkingBudget = budget } : null;
    }

    private static string DescribeOutcome(GeminiCallOutcome outcome, HttpStatusCode statusCode) => outcome.Kind switch
    {
        GeminiOutcomeKind.DailyQuota => "daily free quota used (429)",
        GeminiOutcomeKind.MinuteQuota => "per-minute limit (429)",
        GeminiOutcomeKind.NoFreeTier => "no free-tier quota (429)",
        GeminiOutcomeKind.Overloaded => "high demand (503)",
        GeminiOutcomeKind.ModelGone => "model not found (404)",
        GeminiOutcomeKind.Incompatible => $"request rejected (HTTP {(int)statusCode})",
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

    private sealed record GeminiGenerateResult(string Model, string Json, string? FinishReason);

    /// <summary>True when Gemini stopped for another reason than finishing its answer (MAX_TOKENS, SAFETY, ...).</summary>
    public static bool IsEarlyStop(string? finishReason) =>
        !string.IsNullOrWhiteSpace(finishReason) &&
        !string.Equals(finishReason, "STOP", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(finishReason, "FINISH_REASON_UNSPECIFIED", StringComparison.OrdinalIgnoreCase);

    private static string FormatFinishReason(string? finishReason) =>
        string.IsNullOrWhiteSpace(finishReason) ? string.Empty : $" (finishReason={finishReason})";

    private sealed class GeminiHttpFailureException : Exception
    {
        public GeminiHttpFailureException(HttpStatusCode statusCode, string responseBody)
            : base($"Gemini returned HTTP {(int)statusCode}.")
        {
            StatusCode = statusCode;
            ResponseBody = responseBody;
        }

        public HttpStatusCode StatusCode { get; }

        public string ResponseBody { get; }
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

        public GeminiThinkingConfig? ThinkingConfig { get; set; }
    }

    private sealed class GeminiThinkingConfig
    {
        public string? ThinkingLevel { get; set; }

        public int? ThinkingBudget { get; set; }
    }

    private sealed class GeminiContent
    {
        public List<GeminiPart> Parts { get; set; } = [];
    }

    private sealed class GeminiPart
    {
        public string? Text { get; set; }

        public bool? Thought { get; set; }
    }

    private sealed class GeminiGenerateContentResponse
    {
        public List<GeminiCandidate>? Candidates { get; set; }

        public string? ModelVersion { get; set; }
    }

    private sealed class GeminiCandidate
    {
        public GeminiContent? Content { get; set; }

        public string? FinishReason { get; set; }
    }
}
