using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services.Gemini;
using media_management_app.Services.Gemini.Routing;

namespace MediaManager.App.Tests.Gemini;

/// <summary>
/// The automatic model router end to end: GeminiApiClient + router + quota store + real catalog, with a scripted
/// HTTP handler (no network) and a manual clock (PR-12b).
/// </summary>
public sealed class GeminiModelRouterTests
{
    private const string Best = "gemini-3.8-flash";
    private const string Second = "gemini-3.6-flash";
    private const string Third = "gemini-3.5-flash-lite";

    [Fact]
    public async Task Auto_UsesBestModelFirst_WithThinkingLevel_AndKeyOnlyInHeader()
    {
        using var host = new GeminiTestHost(_ => ScriptedHandler.Success());

        var result = await host.Client.GenerateJsonTextWithModelAsync("system", "{}");

        Assert.Equal(Best, result.Model);
        var request = Assert.Single(host.Handler.Requests);
        Assert.Equal(Best, GeminiTestHost.ModelOf(request));
        Assert.Equal("test-key", request.ApiKeyHeader);
        Assert.DoesNotContain("key=", request.Url);
        Assert.Contains("\"thinkingLevel\":\"low\"", request.Body);
    }

    [Fact]
    public async Task DailyQuota_MovesToNextModel_LearnsLimit_AndLocksUntilRetryDelay()
    {
        using var host = new GeminiTestHost(request => GeminiTestHost.ModelOf(request) == Best
            ? ScriptedHandler.Json(HttpStatusCode.TooManyRequests, GeminiRealResponses.DailyQuotaExceeded)
            : ScriptedHandler.Success());

        var first = await host.Client.GenerateJsonTextWithModelAsync("system", "{}");

        Assert.Equal(Second, first.Model);
        var snapshot = host.Snapshot(Best);
        Assert.Equal(GeminiModelLockReason.DailyQuota, snapshot.LockReason);
        Assert.Equal(20, snapshot.DailyLimit);
        Assert.Equal(1, snapshot.RequestsToday);
        Assert.Equal(GeminiTestHost.Start + TimeSpan.FromSeconds(65065), snapshot.LockedUntilUtc);

        host.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await host.Client.GenerateJsonTextWithModelAsync("system", "{}");

        Assert.Equal(Second, second.Model);
        Assert.Equal(new[] { Best, Second, Second }, host.Handler.Requests.Select(GeminiTestHost.ModelOf));
    }

    [Fact]
    public async Task NoFreeTier_LocksModelUntilCatalogRefresh()
    {
        var bestHasFreeTier = false;
        using var host = new GeminiTestHost(request => GeminiTestHost.ModelOf(request) == Best && !bestHasFreeTier
            ? ScriptedHandler.Json(HttpStatusCode.TooManyRequests, GeminiRealResponses.NoFreeTier)
            : ScriptedHandler.Success());

        Assert.Equal(Second, (await host.Client.GenerateJsonTextWithModelAsync("system", "{}")).Model);

        host.Clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(Second, (await host.Client.GenerateJsonTextWithModelAsync("system", "{}")).Model);
        Assert.Equal(GeminiModelLockReason.NoFreeTier, host.Snapshot(Best).LockReason);

        bestHasFreeTier = true;
        host.Router.ClearPermanentLocks();
        Assert.Equal(Best, (await host.Client.GenerateJsonTextWithModelAsync("system", "{}")).Model);
    }

    [Fact]
    public async Task InvalidKey_StopsAfterOneRequest()
    {
        using var host = new GeminiTestHost(_ => ScriptedHandler.Json(HttpStatusCode.BadRequest, GeminiRealResponses.InvalidApiKey));

        var error = await Assert.ThrowsAsync<GeminiRequestBlockedException>(() =>
            host.Client.GenerateJsonTextWithModelAsync("system", "{}"));

        Assert.Equal(1, host.Handler.Calls);
        Assert.Contains("API key", error.Message);
        Assert.Equal(GeminiModelLockReason.None, host.Snapshot(Best).LockReason);
    }

    // Audit L2-05: "User location is not supported" is about the caller, not the model: stop, lock nothing.
    [Fact]
    public async Task UnsupportedLocation400_StopsAfterOneRequest_AndLocksNoModel()
    {
        using var host = new GeminiTestHost(_ => ScriptedHandler.Json(
            HttpStatusCode.BadRequest,
            """{"error":{"code":400,"message":"User location is not supported for the API use.","status":"FAILED_PRECONDITION"}}"""));

        await Assert.ThrowsAsync<GeminiRequestBlockedException>(() =>
            host.Client.GenerateJsonTextWithModelAsync("system", "{}"));

        Assert.Equal(1, host.Handler.Calls);
        Assert.Equal(GeminiModelLockReason.None, host.Snapshot(Best).LockReason);
    }

    // Audit L2-06: a request that never reached Google (offline, DNS) does not use up the local daily count.
    [Fact]
    public async Task NetworkFailureBeforeAnyResponse_IsNotCountedAgainstTheDailyQuota()
    {
        using var host = new GeminiTestHost(_ => throw new HttpRequestException("No such host is known."));

        await Assert.ThrowsAnyAsync<Exception>(() => host.Client.GenerateJsonTextWithModelAsync("system", "{}"));

        Assert.True(host.Handler.Calls > 0);
        Assert.Equal(0, host.Snapshot(Best).RequestsToday);
    }

    [Fact]
    public async Task PrepayDepleted402_StopsAfterOneRequest()
    {
        using var host = new GeminiTestHost(_ => ScriptedHandler.Json(
            (HttpStatusCode)402,
            """{"error":{"code":402,"message":"Your Prepay credit balance is depleted.","status":"RESOURCE_EXHAUSTED"}}"""));

        await Assert.ThrowsAsync<GeminiRequestBlockedException>(() =>
            host.Client.GenerateJsonTextWithModelAsync("system", "{}"));

        Assert.Equal(1, host.Handler.Calls);
    }

    [Fact]
    public async Task HighDemand_UsesNextModel_ThenReturnsToBestAfterTheLock()
    {
        var overloaded = true;
        using var host = new GeminiTestHost(request => GeminiTestHost.ModelOf(request) == Best && overloaded
            ? ScriptedHandler.Json(HttpStatusCode.ServiceUnavailable, GeminiRealResponses.HighDemand)
            : ScriptedHandler.Success());

        Assert.Equal(Second, (await host.Client.GenerateJsonTextWithModelAsync("system", "{}")).Model);
        Assert.Equal(GeminiModelLockReason.Overloaded, host.Snapshot(Best).LockReason);

        overloaded = false;
        host.Clock.Advance(TimeSpan.FromSeconds(AppConstants.GeminiOverloadedLockSeconds + 1));
        Assert.Equal(Best, (await host.Client.GenerateJsonTextWithModelAsync("system", "{}")).Model);
    }

    [Fact]
    public async Task EveryModelOverloaded_TriesAtMostThreeModels()
    {
        using var host = new GeminiTestHost(_ => ScriptedHandler.Json(HttpStatusCode.ServiceUnavailable, GeminiRealResponses.HighDemand));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.Client.GenerateJsonTextWithModelAsync("system", "{}"));

        Assert.Equal(AppConstants.GeminiMaxModelsPerCall, host.Handler.Calls);
        Assert.Equal(new[] { Best, Second, Third }, host.Handler.Requests.Select(GeminiTestHost.ModelOf));
    }

    [Fact]
    public async Task AllDailyQuotasUsed_FailsWithoutCallingGoogle_AndSaysWhenItFreesUp()
    {
        using var host = new GeminiTestHost(_ => ScriptedHandler.Success());
        foreach (var model in host.Router.GetCandidateModels())
        {
            host.Store.LearnDailyLimit(model, 1);
            host.Store.RecordAttempt(model);
        }

        host.Clock.Advance(TimeSpan.FromMinutes(5));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.Client.GenerateJsonTextWithModelAsync("system", "{}"));

        Assert.Equal(0, host.Handler.Calls);
        Assert.Contains("frees up at", error.Message);
    }

    [Fact]
    public async Task QuotaDay_RollsOverAtPacificMidnight()
    {
        using var host = new GeminiTestHost(_ => ScriptedHandler.Success());
        host.Store.LearnDailyLimit(Best, 1);
        host.Store.RecordAttempt(Best);
        host.Clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(Second, (await host.Client.GenerateJsonTextWithModelAsync("system", "{}")).Model);

        // 14:00 UTC on 12 Nov is 06:00 PST; the quota day ends at 08:00 UTC on 13 Nov.
        host.Clock.UtcNow = new DateTimeOffset(2026, 11, 13, 8, 0, 1, TimeSpan.Zero);
        Assert.Equal(0, host.Snapshot(Best).RequestsToday);
        Assert.Equal(Best, (await host.Client.GenerateJsonTextWithModelAsync("system", "{}")).Model);
    }

    [Fact]
    public void NextPacificMidnight_IsEightUtcInWinter_AndSevenUtcInSummer()
    {
        Assert.Equal(
            new DateTimeOffset(2026, 11, 13, 8, 0, 0, TimeSpan.Zero),
            GeminiModelAvailabilityStore.NextPacificMidnight(new DateTimeOffset(2026, 11, 12, 14, 0, 0, TimeSpan.Zero)));
        Assert.Equal(
            new DateTimeOffset(2026, 7, 2, 7, 0, 0, TimeSpan.Zero),
            GeminiModelAvailabilityStore.NextPacificMidnight(new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task Manual_UsesUserChain_AndSendsNoThinkingConfigTo25FlashLite()
    {
        using var host = new GeminiTestHost(_ => ScriptedHandler.Success(), gemini =>
        {
            gemini.AutoSelectModel = false;
            gemini.Model = "gemini-2.5-flash-lite";
            gemini.FallbackModels = ["gemini-3.1-flash-lite"];
        });

        var result = await host.Client.GenerateJsonTextWithModelAsync("system", "{}");

        Assert.Equal("gemini-2.5-flash-lite", result.Model);
        Assert.DoesNotContain("thinkingConfig", host.Handler.Requests[0].Body);
        Assert.Equal(new[] { "gemini-2.5-flash-lite", "gemini-3.1-flash-lite" }, host.Router.GetCandidateModels());
    }

    [Fact]
    public async Task Manual_UnknownNewerModel_IsKept_AndGetsLowThinking()
    {
        using var host = new GeminiTestHost(_ => ScriptedHandler.Success(), gemini =>
        {
            gemini.AutoSelectModel = false;
            gemini.Model = "gemini-9-flash";
            gemini.FallbackModels = ["gemini-3.5-flash-lite"];
        });

        var result = await host.Client.GenerateJsonTextWithModelAsync("system", "{}");

        Assert.Equal("gemini-9-flash", result.Model);
        Assert.Contains("\"thinkingLevel\":\"low\"", host.Handler.Requests[0].Body);
    }

    [Fact]
    public void Auto_SkipsModelsPastTheirRetireDate()
    {
        using var host = new GeminiTestHost(_ => ScriptedHandler.Success());
        Assert.Contains("gemini-3.1-flash-lite", host.Router.GetCandidateModels());

        host.Clock.UtcNow = new DateTimeOffset(2027, 5, 8, 0, 0, 0, TimeSpan.Zero);

        Assert.DoesNotContain("gemini-3.1-flash-lite", host.Router.GetCandidateModels());
    }

    [Fact]
    public void OldCatalogFile_IsMergedWithNewSeed_KeepingUnknownEntries()
    {
        using var host = new GeminiTestHost(_ => ScriptedHandler.Success(), beforeCatalog: stateFolder =>
        {
            var old = new GeminiModelCatalogFile
            {
                SeedVersion = 0,
                DefaultModel = "gemini-2.5-flash-lite",
                FallbackModels = ["gemini-2.0-flash", "gemini-2.5-flash"],
                Models =
                [
                    new GeminiModelEntry { Id = "gemini-2.0-flash", DisplayName = "2.0 Flash", SupportsTextMapping = true },
                    new GeminiModelEntry { Id = "gemini-x-custom", DisplayName = "Custom", SupportsTextMapping = false }
                ]
            };
            File.WriteAllText(
                Path.Combine(stateFolder, AppConstants.GeminiModelsFileName),
                JsonSerializer.Serialize(old, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        });

        var catalog = host.Catalog.Current;
        Assert.Equal(GeminiModelCatalogDefaultsVersion, catalog.SeedVersion);
        Assert.True(catalog.Models.Single(entry => entry.Id == "gemini-2.0-flash").Deprecated);
        Assert.Contains(catalog.Models, entry => entry.Id == "gemini-x-custom");
        Assert.True(catalog.Models.Single(entry => entry.Id == Best).AutoRoute);
        Assert.DoesNotContain("gemini-2.0-flash", catalog.FallbackModels);
        Assert.Equal(Best, host.Router.GetCandidateModels()[0]);
    }

    private static int GeminiModelCatalogDefaultsVersion => media_management_app.Common.GeminiModelCatalogDefaults.SeedVersion;
}
