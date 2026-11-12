using System.Text.RegularExpressions;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services.Gemini.Routing;

/// <summary>A model the router picked, with the request settings for it.</summary>
public sealed record GeminiRoutedModel(
    string Model,
    TimeSpan Wait,
    string? ThinkingLevel,
    int? ThinkingBudget);

public sealed record GeminiRouteDecision(GeminiRoutedModel? Selected, string? UnavailableReason, DateTimeOffset? NextAvailableUtc);

/// <summary>Status of one model for the Settings screen.</summary>
public sealed record GeminiModelStatus(string Model, string DisplayName, GeminiModelSnapshot Snapshot);

/// <summary>
/// Picks the Gemini model for each request: the best model (catalog rank) that still has free quota today and is
/// not locked, waiting briefly for per-minute spacing; then learns from each response.
/// In manual mode the try-order is the user's model plus fallbacks instead of the catalog.
/// </summary>
public sealed class GeminiModelRouter
{
    private static readonly Regex MajorVersionRegex = new(@"^gemini-(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ISettingsService _settingsService;
    private readonly IGeminiModelCatalogService _catalog;
    private readonly GeminiModelAvailabilityStore _store;
    private readonly IAppLogger _logger;

    public GeminiModelRouter(
        ISettingsService settingsService,
        IGeminiModelCatalogService catalog,
        GeminiModelAvailabilityStore store,
        IAppLogger logger)
    {
        _settingsService = settingsService;
        _catalog = catalog;
        _store = store;
        _logger = logger;
    }

    public bool IsAutomatic => _settingsService.Current.Gemini?.AutoSelectModel ?? true;

    /// <summary>The models the router would try, in order.</summary>
    public IReadOnlyList<string> GetCandidateModels()
    {
        if (IsAutomatic)
        {
            var now = _store.UtcNow;
            return _catalog.Current.Models
                .Select((entry, index) => (entry, index))
                .Where(item => item.entry.AutoRoute && !item.entry.Deprecated && !IsRetired(item.entry, now))
                .OrderBy(item => item.entry.Rank)
                .ThenBy(item => item.index)
                .Select(item => item.entry.Id)
                .ToList();
        }

        var gemini = _settingsService.Current.Gemini;
        var chain = new List<string> { _catalog.Normalize(gemini?.Model) };
        var fallbacks = gemini?.FallbackModels is { Length: > 0 } configured ? configured : _catalog.FallbackModels.ToArray();
        foreach (var fallback in fallbacks)
        {
            if (chain.Count >= AppConstants.GeminiMaxFallbackModels + 1)
            {
                break;
            }

            var normalized = _catalog.Normalize(fallback);
            if (!chain.Contains(normalized, StringComparer.OrdinalIgnoreCase) &&
                GeminiTextModelFilter.IsSuitableForTextJsonMapping(normalized))
            {
                chain.Add(normalized);
            }
        }

        return chain;
    }

    public GeminiRouteDecision Select(IReadOnlySet<string> alreadyTried)
    {
        var now = _store.UtcNow;
        var maxWait = TimeSpan.FromSeconds(AppConstants.GeminiMaxSpacingWaitSeconds);
        GeminiRoutedModel? deferred = null;
        DateTimeOffset? nextAvailable = null;
        var reasons = new List<string>();

        foreach (var model in GetCandidateModels())
        {
            if (alreadyTried.Contains(model))
            {
                continue;
            }

            var entry = FindEntry(model);
            var snapshot = _store.GetSnapshot(model, entry?.FreeRequestsPerDay);
            if (snapshot.LockReason != GeminiModelLockReason.None)
            {
                reasons.Add($"{model}: {DescribeLock(snapshot)}");
                if (snapshot.LockedUntilUtc is { } until)
                {
                    nextAvailable = Min(nextAvailable, until);
                }

                continue;
            }

            if (snapshot.RemainingToday == 0)
            {
                reasons.Add($"{model}: daily quota used ({snapshot.RequestsToday}/{snapshot.DailyLimit})");
                nextAvailable = Min(nextAvailable, GeminiModelAvailabilityStore.NextPacificMidnight(now));
                continue;
            }

            var wait = _store.GetSpacingWait(model, entry?.FreeRequestsPerMinute);
            var routed = BuildRoutedModel(model, entry, wait);
            if (wait <= maxWait)
            {
                return new GeminiRouteDecision(routed, null, null);
            }

            if (deferred is null || wait < deferred.Wait)
            {
                deferred = routed;
            }
        }

        if (deferred is not null)
        {
            return new GeminiRouteDecision(deferred, null, null);
        }

        var reason = reasons.Count == 0
            ? "No Gemini model is available."
            : "No Gemini model is available right now (" + string.Join("; ", reasons) + ").";
        return new GeminiRouteDecision(null, reason, nextAvailable);
    }

    public void RecordAttempt(string model) => _store.RecordAttempt(model);

    /// <summary>Updates the model's state from a failed response. Returns true when the caller should stop.</summary>
    public bool Report(string model, GeminiCallOutcome outcome)
    {
        var now = _store.UtcNow;
        switch (outcome.Kind)
        {
            case GeminiOutcomeKind.DailyQuota:
                if (outcome.QuotaLimit is { } limit)
                {
                    _store.LearnDailyLimit(model, limit);
                }

                var until = outcome.RetryDelay is { } delay
                    ? now + delay
                    : GeminiModelAvailabilityStore.NextPacificMidnight(now);
                _store.Lock(model, GeminiModelLockReason.DailyQuota, until, outcome.Message);
                _logger.Warning(
                    $"Gemini '{model}' daily quota used{(outcome.QuotaLimit is { } learned ? $" (limit {learned}/day)" : string.Empty)}; locked until {until.ToLocalTime():yyyy-MM-dd HH:mm}.",
                    LogTarget.All);
                return false;

            case GeminiOutcomeKind.MinuteQuota:
                _store.Lock(model, GeminiModelLockReason.MinuteQuota, now + (outcome.RetryDelay ?? TimeSpan.FromSeconds(60)), outcome.Message);
                return false;

            case GeminiOutcomeKind.Overloaded:
                _store.Lock(model, GeminiModelLockReason.Overloaded, now.AddSeconds(AppConstants.GeminiOverloadedLockSeconds), outcome.Message);
                return false;

            case GeminiOutcomeKind.ServerError:
                _store.Lock(model, GeminiModelLockReason.ServerError, now.AddSeconds(AppConstants.GeminiServerErrorLockSeconds), outcome.Message);
                return false;

            case GeminiOutcomeKind.NoFreeTier:
                _store.Lock(model, GeminiModelLockReason.NoFreeTier, null, outcome.Message);
                _logger.Warning($"Gemini '{model}' has no free-tier quota on this project; the router stops using it.", LogTarget.All);
                return false;

            case GeminiOutcomeKind.ModelGone:
                _store.Lock(model, GeminiModelLockReason.ModelGone, null, outcome.Message);
                _logger.Warning($"Gemini '{model}' was not found (404); the router stops using it until the model list is refreshed.", LogTarget.All);
                return false;

            case GeminiOutcomeKind.Incompatible:
                _store.Lock(model, GeminiModelLockReason.Incompatible, null, outcome.Message);
                _logger.Warning($"Gemini '{model}' rejected the request (400): {outcome.Message}", LogTarget.All);
                return false;

            case GeminiOutcomeKind.Fatal:
            default:
                return true;
        }
    }

    public IReadOnlyList<GeminiModelStatus> GetStatuses()
    {
        return GetCandidateModels()
            .Select(model =>
            {
                var entry = FindEntry(model);
                return new GeminiModelStatus(
                    model,
                    entry?.DisplayName ?? model,
                    _store.GetSnapshot(model, entry?.FreeRequestsPerDay));
            })
            .ToList();
    }

    public void ClearPermanentLocks() => _store.ClearPermanentLocks();

    public static string DescribeLock(GeminiModelSnapshot snapshot)
    {
        var until = snapshot.LockedUntilUtc is { } time ? $" until {time.ToLocalTime():HH:mm}" : string.Empty;
        return snapshot.LockReason switch
        {
            GeminiModelLockReason.DailyQuota => $"daily quota used{until}",
            GeminiModelLockReason.MinuteQuota => $"per-minute limit{until}",
            GeminiModelLockReason.Overloaded => $"Google overloaded (503/timeout){until}",
            GeminiModelLockReason.ServerError => $"server error{until}",
            GeminiModelLockReason.NoFreeTier => "no free-tier quota (cleared by Refresh models)",
            GeminiModelLockReason.ModelGone => "model not found (cleared by Refresh models)",
            GeminiModelLockReason.Incompatible => "request rejected (cleared by Refresh models)",
            _ => "available"
        };
    }

    private GeminiRoutedModel BuildRoutedModel(string model, GeminiModelEntry? entry, TimeSpan wait)
    {
        if (entry is not null)
        {
            return new GeminiRoutedModel(model, wait, NullIfEmpty(entry.ThinkingLevel), entry.ThinkingBudget);
        }

        // A model the catalog does not describe (manual mode): Gemini 3 and later take a thinking level, and
        // "low" is the one level every Gemini 3/4 model accepts. Older models get no thinking config.
        var match = MajorVersionRegex.Match(model);
        var major = match.Success && int.TryParse(match.Groups[1].Value, out var parsed) ? parsed : 0;
        return new GeminiRoutedModel(model, wait, major >= 3 ? "low" : null, null);
    }

    private GeminiModelEntry? FindEntry(string model) =>
        _catalog.Current.Models.FirstOrDefault(entry => string.Equals(entry.Id, model, StringComparison.OrdinalIgnoreCase));

    private static bool IsRetired(GeminiModelEntry entry, DateTimeOffset now) =>
        entry.RetireAfterUtc is { } retire && now >= new DateTimeOffset(DateTime.SpecifyKind(retire, DateTimeKind.Utc));

    private static DateTimeOffset? Min(DateTimeOffset? current, DateTimeOffset candidate) =>
        current is null || candidate < current ? candidate : current;

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
