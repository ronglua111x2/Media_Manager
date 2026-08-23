namespace media_management_app.Common;

public enum EpisodeAvailability
{
    Missing = 0,
    Available = 1
}

public enum ShowSeriesStatus
{
    Unknown = 0,
    Ongoing = 1,
    Finished = 2
}

public enum UserWatchStatus
{
    None = 0,
    Watching = 1,
    Completed = 2,
    OnHold = 3,
    Dropped = 4,
    PlanToWatch = 5
}
