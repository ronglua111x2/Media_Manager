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

#region Media

public enum MediaKind
{
    Unknown = 0,
    TvEpisode = 1,
    Movie = 2
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

#endregion
