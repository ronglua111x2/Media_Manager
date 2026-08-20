namespace media_management_app.Models;

/// <summary>
/// Configuration for torrent content validation (malware file-list scan).
/// </summary>
public sealed class TorrentValidationConfig
{
    public bool EnableContentValidation { get; set; } = true;

    public List<string> AllowedMediaExtensions { get; set; } =
    [
        ".mkv", ".mp4", ".avi", ".mov", ".flv", ".wmv", ".webm",
        ".m3u8", ".ts", ".m2ts", ".mpg", ".mpeg", ".3gp", ".ogv",
        ".mts", ".mxf", ".vob", ".f4v", ".m4v"
    ];

    /// <summary>
    /// Extensions that fail validation (executables/scripts/installers). Archives are not listed —
    /// some legitimate packs include them and would false-positive.
    /// </summary>
    public List<string> DangerousExtensions { get; set; } =
    [
        ".exe", ".scr", ".bat", ".cmd", ".com", ".pif",
        ".vbs", ".js", ".jar", ".ps1", ".psm1", ".psd1",
        ".dll", ".sys", ".drv", ".ocx", ".cpl",
        ".msi", ".app", ".dmg",
        ".sh", ".bash", ".ksh", ".csh", ".run",
        ".py", ".pyc", ".pyw", ".pl",
        ".apk", ".deb", ".rpm", ".inf"
    ];

    public List<string> CustomDangerousExtensions { get; set; } = [];

    public bool CheckExtensionObfuscation { get; set; } = true;

    /// <summary>
    /// Max seconds to wait for torrent file list after add (while the torrent is running).
    /// </summary>
    public int ValidationTimeoutSeconds { get; set; } = 90;

    public IEnumerable<string> GetAllDangerousExtensions()
    {
        var allDangerous = new HashSet<string>(DangerousExtensions, StringComparer.OrdinalIgnoreCase);
        foreach (var custom in CustomDangerousExtensions)
        {
            if (!string.IsNullOrWhiteSpace(custom))
            {
                allDangerous.Add(custom.Trim());
            }
        }

        return allDangerous;
    }
}
