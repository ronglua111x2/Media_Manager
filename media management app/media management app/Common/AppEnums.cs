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
