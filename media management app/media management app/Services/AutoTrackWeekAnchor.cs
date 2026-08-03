using media_management_app.Models;

namespace media_management_app.Services;

public static class AutoTrackWeekAnchor
{
    public static bool HasCustomSchedule(TrackedShow show) =>
        show.AutoTrackAnchorDayOfWeek is not null ||
        !string.IsNullOrWhiteSpace(show.AutoTrackAnchorTimeLocal);

    public static bool IsWeeklyScheduleEnforced(TrackedShow show, AutoTrackSettings settings) =>
        HasCustomSchedule(show) || settings.EnforceGlobalWeeklySchedule;

    public static (DayOfWeek Day, TimeSpan Time) GetEffectiveAnchor(TrackedShow show, AutoTrackSettings settings)
    {
        var day = show.AutoTrackAnchorDayOfWeek ?? settings.AnchorDayOfWeek;
        var timeText = show.AutoTrackAnchorTimeLocal ?? settings.AnchorTimeLocal;
        return (day, ParseLocalTime(timeText));
    }

    public static bool IsPastAnchorThisWeek(TrackedShow show, DateTime nowLocal, AutoTrackSettings settings)
    {
        if (!IsWeeklyScheduleEnforced(show, settings))
        {
            return true;
        }

        var (day, time) = GetEffectiveAnchor(show, settings);
        var anchorDateTime = GetAnchorDateTimeThisWeek(day, time, nowLocal);
        return nowLocal >= anchorDateTime;
    }

    public static string WeekKey(DateTime dateTimeLocal)
    {
        var weekStart = GetStartOfWeekSunday(dateTimeLocal.Date);
        return weekStart.ToString("yyyy-MM-dd");
    }

    public static string FormatEffectiveAnchor(TrackedShow show, AutoTrackSettings settings)
    {
        var (day, time) = GetEffectiveAnchor(show, settings);
        return $"{day} {time:hh\\:mm}";
    }

    public static DateTime GetAnchorDateTimeThisWeek(DayOfWeek anchorDay, TimeSpan anchorTime, DateTime nowLocal)
    {
        var weekStart = GetStartOfWeekSunday(nowLocal.Date);
        var daysFromSunday = (int)anchorDay;
        return weekStart.AddDays(daysFromSunday).Add(anchorTime);
    }

    public static DateTime ToTimePickerValue(string? timeText)
    {
        return DateTime.Today.Add(ParseLocalTime(timeText));
    }

    public static string FormatTimeLocal(DateTime? time)
    {
        return time?.ToString("HH:mm") ?? "21:00";
    }

    public static TimeSpan ParseLocalTime(string? timeText)
    {
        if (string.IsNullOrWhiteSpace(timeText))
        {
            return new TimeSpan(21, 0, 0);
        }

        var parts = timeText.Trim().Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 &&
            int.TryParse(parts[0], out var hours) &&
            int.TryParse(parts[1], out var minutes))
        {
            return new TimeSpan(Math.Clamp(hours, 0, 23), Math.Clamp(minutes, 0, 59), 0);
        }

        return new TimeSpan(21, 0, 0);
    }

    private static DateTime GetStartOfWeekSunday(DateTime date)
    {
        var diff = (7 + (date.DayOfWeek - DayOfWeek.Sunday)) % 7;
        return date.AddDays(-diff);
    }
}