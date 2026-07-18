namespace media_management_app.Models;

public sealed class AutoTrackSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Legacy field migrated to <see cref="TorrentHuntIntervalMinutes"/> on load.</summary>
    public int IntervalHours { get; set; } = 6;

    public DayOfWeek AnchorDayOfWeek { get; set; } = DayOfWeek.Sunday;

    public string AnchorTimeLocal { get; set; } = "21:00";

    public int TmdbCheckIntervalMinutes { get; set; } = 30;

    public int TorrentHuntIntervalMinutes { get; set; } = 60;

    /// <summary>
    /// Minimum hours after an episode air date before auto-track will hunt for torrents (0 = no delay).
    /// </summary>
    public int HuntMinHoursAfterAirDate { get; set; } = 4;

    public int ReconcileIntervalMinutes { get; set; } = 10;

    public int MaxTmdbRefreshesPerDay { get; set; } = 20;

    public AutoTrackQualityPolicy Quality { get; set; } = new();

    public AutoTrackSearchSettings Search { get; set; } = new();

    public DateTime? LastRunUtc { get; set; }

    public string? LastRunSummary { get; set; }

    /// <summary>Day-scoped TMDB API budget. Replaces legacy LastTmdbRefreshDayKey / TmdbRefreshesToday.</summary>
    public TmdbDailyBudget DailyBudget { get; set; } = new();

    /// <summary>Legacy; migrated into <see cref="DailyBudget"/> on load.</summary>
    public string? LastTmdbRefreshDayKey { get; set; }

    /// <summary>Legacy; migrated into <see cref="DailyBudget"/> on load.</summary>
    public int TmdbRefreshesToday { get; set; }
}
