namespace media_management_app.Services;

/// <summary>App display/schedule clock: fixed UTC+7 (SE Asia / Vietnam).</summary>
public static class AppTimeZone
{
    public static readonly TimeSpan UtcOffset = TimeSpan.FromHours(7);

    public static DateTime Now => DateTime.UtcNow + UtcOffset;

    public static DateTime Today => Now.Date;

    public static string FormatLongDate(DateTime dateTimeUtcPlus7) =>
        dateTimeUtcPlus7.ToString("dddd, MMMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture);
}
