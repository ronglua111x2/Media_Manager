using System.Net;
using media_management_app.Services.Gemini.Routing;

namespace MediaManager.App.Tests.Gemini;

/// <summary>Classifying Gemini error responses, mostly with bodies captured from the real API (PR-12b).</summary>
public sealed class GeminiErrorClassifierTests
{
    [Fact]
    public void RealDailyQuota429_IsDailyQuota_WithLimitAndRetryDelay()
    {
        var outcome = GeminiErrorClassifier.Classify(HttpStatusCode.TooManyRequests, GeminiRealResponses.DailyQuotaExceeded);

        Assert.Equal(GeminiOutcomeKind.DailyQuota, outcome.Kind);
        Assert.Equal(20, outcome.QuotaLimit);
        Assert.Equal(TimeSpan.FromSeconds(65065), outcome.RetryDelay);
    }

    [Fact]
    public void RealLimitZero429_IsNoFreeTier_EvenWithoutQuotaValue()
    {
        var outcome = GeminiErrorClassifier.Classify(HttpStatusCode.TooManyRequests, GeminiRealResponses.NoFreeTier);

        Assert.Equal(GeminiOutcomeKind.NoFreeTier, outcome.Kind);
    }

    [Fact]
    public void RealInvalidKey400_IsFatal()
    {
        var outcome = GeminiErrorClassifier.Classify(HttpStatusCode.BadRequest, GeminiRealResponses.InvalidApiKey);

        Assert.Equal(GeminiOutcomeKind.Fatal, outcome.Kind);
        Assert.Contains("API key", outcome.Message);
    }

    [Fact]
    public void RealHighDemand503_IsOverloaded()
    {
        var outcome = GeminiErrorClassifier.Classify(HttpStatusCode.ServiceUnavailable, GeminiRealResponses.HighDemand);

        Assert.Equal(GeminiOutcomeKind.Overloaded, outcome.Kind);
    }

    [Fact]
    public void PerMinute429_IsMinuteQuota_WithRetryDelay()
    {
        const string body = """
            {"error":{"code":429,"message":"Quota exceeded for metric: generativelanguage.googleapis.com/generate_content_free_tier_requests, limit: 15, model: gemini-3.5-flash-lite. Please retry in 7.5s.","status":"RESOURCE_EXHAUSTED",
             "details":[{"@type":"type.googleapis.com/google.rpc.QuotaFailure","violations":[{"quotaId":"GenerateRequestsPerMinutePerProjectPerModel-FreeTier","quotaValue":"15"}]},
                        {"@type":"type.googleapis.com/google.rpc.RetryInfo","retryDelay":"7.5s"}]}}
            """;

        var outcome = GeminiErrorClassifier.Classify(HttpStatusCode.TooManyRequests, body);

        Assert.Equal(GeminiOutcomeKind.MinuteQuota, outcome.Kind);
        Assert.Equal(TimeSpan.FromSeconds(7.5), outcome.RetryDelay);
    }

    [Fact]
    public void Quota429WithoutDetails_UsesRetryInMessage()
    {
        const string body = """{"error":{"code":429,"message":"Resource exhausted. Please retry in 18h4m25.45s.","status":"RESOURCE_EXHAUSTED"}}""";

        var outcome = GeminiErrorClassifier.Classify(HttpStatusCode.TooManyRequests, body);

        Assert.Equal(GeminiOutcomeKind.DailyQuota, outcome.Kind);
        Assert.Equal(TimeSpan.FromSeconds(18 * 3600 + 4 * 60 + 25.45), outcome.RetryDelay);
    }

    [Fact]
    public void PrepayDepleted402_IsFatal()
    {
        const string body = """{"error":{"code":402,"message":"Your Prepay credit balance is depleted.","status":"RESOURCE_EXHAUSTED"}}""";

        var outcome = GeminiErrorClassifier.Classify((HttpStatusCode)402, body);

        Assert.Equal(GeminiOutcomeKind.Fatal, outcome.Kind);
        Assert.Contains("free-tier project", outcome.Message);
    }

    [Fact]
    public void VerificationRequired403_IsFatal_AndKeepsGoogleMessage()
    {
        const string body = """
            {"error":{"code":403,"message":"Project 123456789012 must be verified before it can use the Gemini API Free tier. Verify the project at https://aistudio.google.com/verify?project=123456789012.","status":"PERMISSION_DENIED",
             "details":[{"@type":"type.googleapis.com/google.rpc.ErrorInfo","reason":"FREE_TIER_VERIFICATION_REQUIRED"}]}}
            """;

        var outcome = GeminiErrorClassifier.Classify(HttpStatusCode.Forbidden, body);

        Assert.Equal(GeminiOutcomeKind.Fatal, outcome.Kind);
        Assert.Contains("aistudio.google.com/verify", outcome.Message);
    }

    [Fact]
    public void Other400_IsIncompatible_NotFatal()
    {
        const string body = """{"error":{"code":400,"message":"Invalid value at 'generation_config.thinking_config.thinking_level'","status":"INVALID_ARGUMENT"}}""";

        var outcome = GeminiErrorClassifier.Classify(HttpStatusCode.BadRequest, body);

        Assert.Equal(GeminiOutcomeKind.Incompatible, outcome.Kind);
    }

    // Audit L2-05: request-level failures stop the call; they must not lock every model for good.
    [Fact]
    public void FailedPrecondition400_IsFatal_WhateverTheMessage()
    {
        const string body = """{"error":{"code":400,"message":"User location is not supported for the API use.","status":"FAILED_PRECONDITION"}}""";

        Assert.Equal(GeminiOutcomeKind.Fatal, GeminiErrorClassifier.Classify(HttpStatusCode.BadRequest, body).Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.ProxyAuthenticationRequired)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(HttpStatusCode.Redirect)]
    public void UnlistedStatus_IsFatal_NotAPermanentModelLock(HttpStatusCode status)
    {
        Assert.Equal(GeminiOutcomeKind.Fatal, GeminiErrorClassifier.Classify(status, "<html>not json</html>").Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, GeminiOutcomeKind.ModelGone)]
    [InlineData(HttpStatusCode.InternalServerError, GeminiOutcomeKind.ServerError)]
    [InlineData(HttpStatusCode.GatewayTimeout, GeminiOutcomeKind.ServerError)]
    [InlineData(HttpStatusCode.RequestTimeout, GeminiOutcomeKind.ServerError)]
    public void StatusOnly_MapsByStatusCode(HttpStatusCode status, GeminiOutcomeKind expected)
    {
        Assert.Equal(expected, GeminiErrorClassifier.Classify(status, "<html>not json</html>").Kind);
    }

    [Theory]
    [InlineData("65065s", 65065)]
    [InlineData("4.5s", 4.5)]
    [InlineData("500ms", 0.5)]
    [InlineData("18h4m25.45s", 65065.45)]
    [InlineData("1m", 60)]
    public void ParseDuration_ReadsGoogleDurations(string value, double expectedSeconds)
    {
        Assert.Equal(expectedSeconds, GeminiErrorClassifier.ParseDuration(value)!.Value.TotalSeconds, precision: 3);
    }
}
