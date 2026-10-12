namespace media_management_app.Common;

internal static class WindowIcons
{
    public static Uri App { get; } = Pack("Assets/app-icon.ico");
    public static Uri ConsoleLog { get; } = Pack("Assets/window-icons/console-log.ico");
    public static Uri Qbittorrent { get; } = Pack("Assets/window-icons/qbittorrent.ico");
    public static Uri Jellyfin { get; } = Pack("Assets/window-icons/jellyfin.ico");

    public static Uri GetPackUri(WindowIconKind kind) => kind switch
    {
        WindowIconKind.ConsoleLog => ConsoleLog,
        WindowIconKind.Qbittorrent => Qbittorrent,
        WindowIconKind.Jellyfin => Jellyfin,
        _ => App
    };

    private static Uri Pack(string relativePath) =>
        new($"pack://application:,,,/{relativePath}", UriKind.Absolute);
}
