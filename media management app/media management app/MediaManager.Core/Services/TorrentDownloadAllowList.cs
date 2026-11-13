using media_management_app.Models;

namespace media_management_app.Services;

/// <summary>
/// S0 (SEC-A04): which files of a validated torrent qBittorrent should download. Media Manager links only videos;
/// subtitles and external audio/subtitle tracks are kept for playback from the download folder. Everything else
/// (.url, .nfo, images, archives, text) gets priority 0 and never reaches the disk, so a web shortcut with a UNC icon
/// or a password archive next to a real release is never written.
/// </summary>
public static class TorrentDownloadAllowList
{
    private static readonly HashSet<string> CompanionExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".srt", ".ass", ".ssa", ".sub", ".idx", ".sup", ".vtt", ".mka", ".mks"
    };

    public static bool IsWanted(string path, TorrentValidationConfig config)
    {
        var extension = Path.GetExtension(TorrentContentValidationService.NormalizeFileName(path ?? string.Empty));
        if (string.IsNullOrEmpty(extension))
        {
            return false;
        }

        return CompanionExtensions.Contains(extension) ||
               config.AllowedMediaExtensions.Any(allowed =>
                   string.Equals(allowed.StartsWith('.') ? allowed : "." + allowed, extension, StringComparison.OrdinalIgnoreCase));
    }
}
