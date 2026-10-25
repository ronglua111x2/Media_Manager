using media_management_app.Common;

namespace media_management_app.Services;

/// <summary>
/// The single-file build extracts its native DLLs to %TEMP%\.net\media management app\&lt;hash&gt;.
/// Storage Sense / cleanmgr delete temp files nobody holds open. DLLs WPF loads lazily (PenImc_cor3.dll on the
/// first tablet/device-change message) were deleted while the app ran and it died with DllNotFoundException
/// (crash 2026-10-23). Holding a handle without FILE_SHARE_DELETE keeps every extracted DLL in place for the
/// session. The host re-extracts missing files on the next start.
/// </summary>
public static class BundleExtractionGuard
{
    private static readonly List<FileStream> Handles = new();

    public static void Run(IAppLogger logger)
    {
        // Assembly.Location is empty only when running from a single-file bundle.
        if (!string.IsNullOrEmpty(typeof(BundleExtractionGuard).Assembly.Location))
        {
            return;
        }

        var directory = FindExtractionDirectory();
        if (directory is null)
        {
            logger.Warning("Single-file extraction folder not found; native DLLs are not protected from temp cleanup.", LogTarget.File);
            return;
        }

        var failed = 0;
        foreach (var path in Directory.EnumerateFiles(directory, "*.dll", SearchOption.AllDirectories))
        {
            try
            {
                Handles.Add(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed++;
                logger.Warning($"Could not hold extracted DLL '{path}': {ex.Message}", LogTarget.File);
            }
        }

        logger.Info(
            $"Holding {Handles.Count} extracted native DLL(s) in '{directory}' for this session{(failed > 0 ? $" ({failed} failed)" : string.Empty)}.",
            LogTarget.File);
    }

    private static string? FindExtractionDirectory()
    {
        var baseDirectory = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        var searchDirectories = AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string ?? string.Empty;
        return searchDirectories
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(Path.TrimEndingDirectorySeparator)
            .FirstOrDefault(directory =>
                !string.Equals(directory, baseDirectory, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(directory) &&
                File.Exists(Path.Combine(directory, "PresentationNative_cor3.dll")));
    }
}
