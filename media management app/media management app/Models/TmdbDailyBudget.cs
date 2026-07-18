namespace media_management_app.Models;

/// <summary>
/// Day-scoped TMDB refresh budget. Resets automatically when the calendar day changes.
/// </summary>
public sealed class TmdbDailyBudget
{
    public string DayKey { get; set; } = string.Empty;

    public int Used { get; set; }

    public bool IsExpiredFor(DateTime nowLocal)
    {
        var todayKey = nowLocal.ToString("yyyy-MM-dd");
        return !string.Equals(DayKey, todayKey, StringComparison.Ordinal);
    }

    public int Remaining(int max, DateTime nowLocal)
    {
        if (IsExpiredFor(nowLocal))
        {
            return Math.Max(0, max);
        }

        return Math.Max(0, max - Used);
    }

    /// <summary>
    /// Consumes one refresh token for today. Returns false when the daily budget is exhausted.
    /// </summary>
    public bool TryConsume(int max, DateTime nowLocal)
    {
        if (IsExpiredFor(nowLocal))
        {
            DayKey = nowLocal.ToString("yyyy-MM-dd");
            Used = 0;
        }

        if (Used >= max)
        {
            return false;
        }

        Used++;
        return true;
    }
}
