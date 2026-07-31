namespace media_management_app.Common;

public enum NotificationKind
{
    Test = 0,
    AutoTrackNewEpisode = 1,
    AutoTrackHuntProgress = 2,
    AutoTrackHardlinked = 3,
    AutoTrackRunSummary = 4,
    SymlinkCreated = 5,
    WarpRecovered = 6,
    WarpDisconnected = 7,
    JellyfinPathNotified = 8,
    JellyfinRefreshWindowEnded = 9
}
