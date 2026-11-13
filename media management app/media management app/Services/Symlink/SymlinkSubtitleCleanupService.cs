using media_management_app.Common;

namespace media_management_app.Services.Symlink;

/// <summary>
/// Implementation for cleaning up subtitle and companion files associated with symlinks.
/// Searches for files matching the symlink base name with various subtitle extensions.
/// </summary>
public sealed class SymlinkSubtitleCleanupService : ISymlinkSubtitleCleanupService
{
    private static readonly string[] SubtitleExtensions = [".srt", ".ass", ".ssa", ".sub", ".vtt", ".sbv", ".json"];

    private readonly IAppLogger _logger;

    public SymlinkSubtitleCleanupService(IAppLogger logger)
    {
        _logger = logger;
    }

    public void DeleteSubtitleFiles(string symlinkPath)
    {
        if (string.IsNullOrWhiteSpace(symlinkPath) || !File.Exists(symlinkPath))
        {
            return;
        }

        try
        {
            var deletedCount = 0;
            foreach (var file in FindSubtitleFiles(symlinkPath))
            {
                TryDeleteSubtitleFile(file, ref deletedCount);
            }

            if (deletedCount > 0)
            {
                _logger.Info(
                    $"Deleted {deletedCount} subtitle file(s) for {Path.GetFileName(symlinkPath)}",
                    LogTarget.File | LogTarget.Console);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(
                $"Failed to cleanup subtitle files for {Path.GetFileName(symlinkPath)}: {ex.Message}",
                LogTarget.All);
        }
    }

    public IReadOnlyList<string> FindSubtitleFiles(string symlinkPath)
    {
        var directory = Path.GetDirectoryName(symlinkPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        var baseNameWithoutExt = Path.GetFileNameWithoutExtension(symlinkPath);
        var subtitles = new List<string>();

        // Search for all subtitle files with pattern: {baseNameWithoutExt}.*.{subtitleExt}
        // Examples: "Show - S01E01.eng.srt", "Show - S01E01.jpn.ass", "Movie Title.vi.srt"
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(file);

            // Check if file matches pattern: same base name + subtitle extension
            if (!string.Equals(fileNameWithoutExt, baseNameWithoutExt, StringComparison.OrdinalIgnoreCase))
            {
                // Also check for language-suffixed subtitles: "Title.lang.srt"
                // Pattern: {baseNameWithoutExt}.{languageCode}.{subtitleExt}
                var lastDotIndex = fileNameWithoutExt.LastIndexOf('.');
                if (lastDotIndex <= 0)
                {
                    continue;
                }

                var baseWithoutLang = fileNameWithoutExt[..lastDotIndex];
                if (!string.Equals(baseWithoutLang, baseNameWithoutExt, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            if (IsSubtitleExtension(Path.GetExtension(file)))
            {
                subtitles.Add(file);
            }
        }

        return subtitles;
    }

    private static bool IsSubtitleExtension(string extension)
    {
        return SubtitleExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private void TryDeleteSubtitleFile(string filePath, ref int deletedCount)
    {
        try
        {
            File.Delete(filePath);
            deletedCount++;
            _logger.Debug($"Deleted subtitle file: {filePath}", LogTarget.File | LogTarget.Console);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to delete subtitle file {filePath}: {ex.Message}", LogTarget.All);
        }
    }
}
