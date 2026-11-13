using media_management_app.Search;

namespace media_management_app.Services;

public enum SearchSiteSeverity
{
    /// <summary>The site answers searches.</summary>
    Ok,

    /// <summary>The site answers some searches (Auto-Track share used, rate-limit cooldown, half-open test).</summary>
    Limited,

    /// <summary>The site is skipped (daily limit, breaker open, all searches paused).</summary>
    Unavailable,

    /// <summary>Turned off in Settings.</summary>
    Off
}

public sealed record SearchSiteStatus(string Text, SearchSiteSeverity Severity, int UsedToday);

/// <summary>
/// Plain-language state of one native search site for the UI (Settings → Search engines, the quota pill on the
/// Auto, News and Cart screens). Daily budgets reset at midnight UTC (07:00 in Vietnam).
/// </summary>
public static class NativeSearchHealth
{
    public static DateTime NextResetLocal(DateTimeOffset nowUtc) =>
        DateTime.SpecifyKind(nowUtc.UtcDateTime.Date.AddDays(1), DateTimeKind.Utc).ToLocalTime();

    /// <summary>Requests used today; 0 when the stored day is not today (the budget rolled over).</summary>
    public static int UsedToday(IndexerStateSnapshot? state, DateTimeOffset nowUtc) =>
        state is not null && string.Equals(state.DayKey, nowUtc.UtcDateTime.ToString("yyyy-MM-dd"), StringComparison.Ordinal)
            ? state.RequestsToday
            : 0;

    public static int BackgroundBudget(IndexerPolicy policy) => (int)Math.Floor(policy.DailyBudget * policy.BackgroundBudgetShare);

    public static SearchSiteStatus Describe(IndexerStateSnapshot? state, IndexerPolicy policy, bool enabled, bool pausedAll, DateTimeOffset nowUtc)
    {
        var used = UsedToday(state, nowUtc);
        if (!enabled)
        {
            return new SearchSiteStatus("Off (Settings)", SearchSiteSeverity.Off, used);
        }

        if (pausedAll)
        {
            return new SearchSiteStatus("Paused: all native searches are stopped", SearchSiteSeverity.Unavailable, used);
        }

        var reset = NextResetLocal(nowUtc);
        if (state?.Breaker == IndexerBreakerState.Open && state.OpenUntilUtc is { } openUntil && openUntil > nowUtc)
        {
            return new SearchSiteStatus($"Blocked until {openUntil.ToLocalTime():HH:mm} (site refused or failed)", SearchSiteSeverity.Unavailable, used);
        }

        if (used >= policy.DailyBudget)
        {
            return new SearchSiteStatus($"Daily limit reached, resets {reset:HH:mm}", SearchSiteSeverity.Unavailable, used);
        }

        if (state?.Breaker == IndexerBreakerState.HalfOpen)
        {
            return new SearchSiteStatus("Testing again after a block", SearchSiteSeverity.Limited, used);
        }

        if (state?.CooldownUntilUtc is { } cooldown && cooldown > nowUtc)
        {
            return new SearchSiteStatus($"Rate limited until {cooldown.ToLocalTime():HH:mm:ss}", SearchSiteSeverity.Limited, used);
        }

        if (used >= BackgroundBudget(policy))
        {
            return new SearchSiteStatus($"Auto-Track share used; manual searches only until {reset:HH:mm}", SearchSiteSeverity.Limited, used);
        }

        return new SearchSiteStatus(
            state?.LastSuccessUtc is { } ok ? $"Ready (last answer {ok.ToLocalTime():HH:mm})" : "Ready",
            SearchSiteSeverity.Ok,
            used);
    }
}
