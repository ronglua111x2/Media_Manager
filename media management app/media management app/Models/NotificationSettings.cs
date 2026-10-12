namespace media_management_app.Models;

public sealed class NotificationSettings
{
    /// <summary>Key = <see cref="Common.NotificationKind"/> name, e.g. "WarpRecovered". Missing key = enabled.</summary>
    public Dictionary<string, bool> EnabledByKind { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
