using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace media_management_app.Services.Gemini.Routing;

public enum GeminiOutcomeKind
{
    /// <summary>Daily request quota of this model is used up (429, quota id "PerDay").</summary>
    DailyQuota,

    /// <summary>Per-minute quota of this model is used up (429, quota id "PerMinute").</summary>
    MinuteQuota,

    /// <summary>The model has no free-tier quota on this project (429 with "limit: 0").</summary>
    NoFreeTier,

    /// <summary>Google is out of capacity for this model (503 "high demand") or the call timed out.</summary>
    Overloaded,

    /// <summary>Other server-side error (500, 502, 504, 408): retry once, then use another model.</summary>
    ServerError,

    /// <summary>The model does not exist any more (404).</summary>
    ModelGone,

    /// <summary>The request does not fit this model (400 that is not about the API key).</summary>
    Incompatible,

    /// <summary>
    /// Stop: no other model can help (invalid or blocked key, project needs verification, Prepay credits
    /// depleted, free tier unavailable in the region).
    /// </summary>
    Fatal
}

public sealed record GeminiCallOutcome(
    GeminiOutcomeKind Kind,
    string Message,
    TimeSpan? RetryDelay = null,
    int? QuotaLimit = null);

/// <summary>
/// Reads a failed Gemini API response (status code and JSON error body) and decides what the router does next.
/// </summary>
/// <remarks>
/// The quota rules follow <c>classifyGoogleError</c> in google-gemini/gemini-cli
/// (packages/core/src/utils/googleQuotaErrors.ts, Apache-2.0), rewritten in C#. Additions: the learned limit
/// (<c>quotaValue</c>), 400 <c>API_KEY_INVALID</c>, and 402 (Prepay credits depleted). Error bodies captured from
/// the real API on 12 Nov 2026 are the test fixtures.
/// </remarks>
public static class GeminiErrorClassifier
{
    private static readonly TimeSpan LongDelayThreshold = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultMinuteDelay = TimeSpan.FromSeconds(60);

    private static readonly Regex LimitZeroRegex = new(@"limit:\s*0(?![\d.])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RetryInRegex = new(
        @"retry in\s+((?:\d+(?:\.\d+)?(?:h|ms|m|s))+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DurationPartRegex = new(@"(\d+(?:\.\d+)?)(h|ms|m|s)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SpendCapRegex = new(@"\b(spending|spend)\s+cap\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static GeminiCallOutcome Classify(HttpStatusCode statusCode, string? responseBody)
    {
        var error = ParseError(responseBody);
        var message = string.IsNullOrWhiteSpace(error.Message)
            ? $"Gemini returned HTTP {(int)statusCode}."
            : error.Message.Trim();

        switch ((int)statusCode)
        {
            case 400:
                if (error.Reasons.Contains("API_KEY_INVALID", StringComparer.OrdinalIgnoreCase) ||
                    message.Contains("API key not valid", StringComparison.OrdinalIgnoreCase))
                {
                    return new GeminiCallOutcome(GeminiOutcomeKind.Fatal, "The Gemini API key is not valid. Check the key in Settings.");
                }

                // FAILED_PRECONDITION is about the caller (free tier not available, "User location is not
                // supported"), not the model: every model would answer the same, so stop without a lock (audit L2-05).
                if (string.Equals(error.Status, "FAILED_PRECONDITION", StringComparison.OrdinalIgnoreCase))
                {
                    return new GeminiCallOutcome(GeminiOutcomeKind.Fatal, message);
                }

                return new GeminiCallOutcome(GeminiOutcomeKind.Incompatible, message);

            case 401:
                return new GeminiCallOutcome(GeminiOutcomeKind.Fatal, message);

            case 402:
                return new GeminiCallOutcome(
                    GeminiOutcomeKind.Fatal,
                    "Gemini returned 402: the billing account of this key's project has no Prepay credits left. " +
                    "That project is on the paid tier; use a key from a free-tier project instead.");

            case 403:
                return new GeminiCallOutcome(GeminiOutcomeKind.Fatal, message);

            case 404:
                return new GeminiCallOutcome(GeminiOutcomeKind.ModelGone, message);

            case 408:
                return new GeminiCallOutcome(GeminiOutcomeKind.ServerError, message);

            case 429:
                return ClassifyQuota(error, message);

            case 503:
                return new GeminiCallOutcome(GeminiOutcomeKind.Overloaded, message);

            case >= 500:
                return new GeminiCallOutcome(GeminiOutcomeKind.ServerError, message);

            default:
                // 3xx, 407 (proxy), 413 and other statuses come from the network path or the request, not the
                // model. A permanent "Incompatible" lock on each model tried would disable them all (audit L2-05).
                return new GeminiCallOutcome(GeminiOutcomeKind.Fatal, message);
        }
    }

    private static GeminiCallOutcome ClassifyQuota(ParsedError error, string message)
    {
        var retryDelay = error.RetryDelay ?? ParseRetryInFromMessage(message);

        if (LimitZeroRegex.IsMatch(message) || error.Violations.Any(violation => violation.QuotaValue == 0))
        {
            return new GeminiCallOutcome(GeminiOutcomeKind.NoFreeTier, "This model has no free-tier quota on this project.", retryDelay);
        }

        if (SpendCapRegex.IsMatch(message))
        {
            return new GeminiCallOutcome(GeminiOutcomeKind.Fatal, message);
        }

        var daily = error.Violations.FirstOrDefault(violation => IsDaily(violation.QuotaId));
        if (daily is not null)
        {
            return new GeminiCallOutcome(GeminiOutcomeKind.DailyQuota, message, retryDelay, daily.QuotaValue);
        }

        var perMinute = error.Violations.FirstOrDefault(violation => IsPerMinute(violation.QuotaId));
        if (perMinute is not null)
        {
            return new GeminiCallOutcome(GeminiOutcomeKind.MinuteQuota, message, retryDelay ?? DefaultMinuteDelay, perMinute.QuotaValue);
        }

        if (retryDelay is { } delay && delay > LongDelayThreshold)
        {
            return new GeminiCallOutcome(GeminiOutcomeKind.DailyQuota, message, delay);
        }

        return new GeminiCallOutcome(GeminiOutcomeKind.MinuteQuota, message, retryDelay ?? DefaultMinuteDelay);
    }

    private static bool IsDaily(string? quotaId) =>
        quotaId is not null &&
        (quotaId.Contains("PerDay", StringComparison.OrdinalIgnoreCase) ||
         quotaId.Contains("Daily", StringComparison.OrdinalIgnoreCase));

    private static bool IsPerMinute(string? quotaId) =>
        quotaId is not null && quotaId.Contains("PerMinute", StringComparison.OrdinalIgnoreCase);

    /// <summary>Parses Google duration strings such as "65065s", "4.5s", "500ms" or "18h4m25.45s".</summary>
    public static TimeSpan? ParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var matches = DurationPartRegex.Matches(value.Trim());
        if (matches.Count == 0)
        {
            return null;
        }

        double totalSeconds = 0;
        foreach (Match match in matches)
        {
            if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return null;
            }

            totalSeconds += match.Groups[2].Value.ToLowerInvariant() switch
            {
                "h" => number * 3600,
                "m" => number * 60,
                "ms" => number / 1000,
                _ => number
            };
        }

        return TimeSpan.FromSeconds(totalSeconds);
    }

    private static TimeSpan? ParseRetryInFromMessage(string message)
    {
        var match = RetryInRegex.Match(message);
        return match.Success ? ParseDuration(match.Groups[1].Value) : null;
    }

    private static ParsedError ParseError(string? body)
    {
        var parsed = new ParsedError();
        if (string.IsNullOrWhiteSpace(body))
        {
            return parsed;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("error", out var error) ||
                error.ValueKind != JsonValueKind.Object)
            {
                return parsed;
            }

            parsed.Message = GetString(error, "message");
            parsed.Status = GetString(error, "status");

            if (!error.TryGetProperty("details", out var details) || details.ValueKind != JsonValueKind.Array)
            {
                return parsed;
            }

            foreach (var detail in details.EnumerateArray())
            {
                if (detail.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var type = GetString(detail, "@type") ?? string.Empty;
                if (type.EndsWith("google.rpc.QuotaFailure", StringComparison.Ordinal) &&
                    detail.TryGetProperty("violations", out var violations) &&
                    violations.ValueKind == JsonValueKind.Array)
                {
                    foreach (var violation in violations.EnumerateArray())
                    {
                        if (violation.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        parsed.Violations.Add(new QuotaViolation(
                            GetString(violation, "quotaId"),
                            ParseInt(GetString(violation, "quotaValue"))));
                    }
                }
                else if (type.EndsWith("google.rpc.RetryInfo", StringComparison.Ordinal))
                {
                    parsed.RetryDelay = ParseDuration(GetString(detail, "retryDelay"));
                }
                else if (type.EndsWith("google.rpc.ErrorInfo", StringComparison.Ordinal))
                {
                    var reason = GetString(detail, "reason");
                    if (!string.IsNullOrWhiteSpace(reason))
                    {
                        parsed.Reasons.Add(reason);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON (for example an HTML error page): classify by status code only.
        }

        return parsed;
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    private sealed class ParsedError
    {
        public string? Message { get; set; }

        public string? Status { get; set; }

        public TimeSpan? RetryDelay { get; set; }

        public List<QuotaViolation> Violations { get; } = [];

        public List<string> Reasons { get; } = [];
    }

    private sealed record QuotaViolation(string? QuotaId, int? QuotaValue);
}
