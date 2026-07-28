using media_management_app.Models;

namespace media_management_app.Common;

public sealed record NotificationPreferenceDefinition(
    NotificationKind Kind,
    string Title,
    string Description);

public static class NotificationCatalog
{
    public static IReadOnlyList<NotificationPreferenceDefinition> Preferences { get; } =
    [
        new(
            NotificationKind.AutoTrackNewEpisode,
            "Auto-Track: new episode",
            "When TMDB refresh finds a new pending episode for a tracked show."),
        new(
            NotificationKind.AutoTrackHuntProgress,
            "Auto-Track: hunt progress",
            "Hunt/stage updates: skipped folder, resume, search batch, quality filter, candidate pick, torrent added."),
        new(
            NotificationKind.AutoTrackHardlinked,
            "Auto-Track: hardlinked",
            "When an episode is hardlinked into the library."),
        new(
            NotificationKind.AutoTrackRunSummary,
            "Auto-Track: run summary",
            "End-of-run summary toast after an Auto-Track cycle."),
        new(
            NotificationKind.SymlinkCreated,
            "Symlink created / repaired",
            "When a media symlink is created or repaired."),
        new(
            NotificationKind.WarpRecovered,
            "WARP connected",
            "When the app connects WARP (owned session) for SSL recovery or hunt."),
        new(
            NotificationKind.WarpDisconnected,
            "WARP disconnected",
            "When the app disconnects an owned WARP session.")
    ];

    public static bool IsUserToggleable(NotificationKind kind) => kind != NotificationKind.Test;

    /// <summary>Missing key defaults to enabled so new kinds stay ON for existing users.</summary>
    public static bool IsEnabled(NotificationSettings? settings, NotificationKind kind)
    {
        if (!IsUserToggleable(kind))
        {
            return true;
        }

        var map = settings?.EnabledByKind;
        if (map is null || map.Count == 0)
        {
            return true;
        }

        var key = kind.ToString();
        return !map.TryGetValue(key, out var enabled) || enabled;
    }
}
