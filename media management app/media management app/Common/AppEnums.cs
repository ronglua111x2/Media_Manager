namespace media_management_app.Common;

#region Logging

[Flags]
public enum LogTarget
{
    None = 0,
    File = 1,
    Ui = 2,
    Console = 4,
    All = File | Ui | Console
}

public enum AppLogLevel
{
    Trace,
    Debug,
    Info,
    Warning,
    Error,
    Critical
}

#endregion

#region Settings

public enum SettingsSection
{
    System = 0,
    Library = 1,
    Integrations = 2,
    TorrentStorage = 3,
    Notifications = 4,
    Warp = 5
}

#endregion

#region Media

public enum MediaKind
{
    Unknown = 0,
    TvEpisode = 1,
    Movie = 2,
    TvSeasonPack = 3
}

public enum ParserPattern
{
    Unknown = 0,
    StandardTv = 1,
    AnimeAbsolute = 2,
    MovieWithYear = 3,
    Ignored = 4
}

public enum LibraryRootMode
{
    AutoPerDrive = 0
}

public enum EpisodeAvailability
{
    Missing = 0,
    Available = 1
}

public enum FetchJobStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Canceled = 4
}

public enum EpisodeFetchStatus
{
    NotFetched = 0,
    Pending = 1,
    Searching = 2,
    CandidatesFound = 3,
    NoCandidates = 4,
    Added = 5,
    Error = 6
}

public enum SeasonManagementMode
{
    Episode = 0,
    Pack = 1
}

public enum ShowSeriesStatus
{
    Unknown = 0,
    Ongoing = 1,
    Finished = 2
}

public enum AutoTorrentLinkKind
{
    Episode = 1,
    SeasonPack = 2,
    Movie = 3
}

public enum TorrentOrderStatus
{
    Draft = 0,
    Searching = 1,
    CandidatesFound = 2,
    NoCandidates = 3,
    Approved = 4,
    AddedToClient = 5,
    Downloading = 6,
    Completed = 7,
    Failed = 8,
    Canceled = 9
}

#endregion
