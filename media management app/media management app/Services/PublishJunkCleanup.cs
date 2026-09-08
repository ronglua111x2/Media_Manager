using System.Diagnostics;
using media_management_app.Common;

namespace media_management_app.Services;

public static class PublishJunkCleanup
{
    private static readonly HashSet<string> JunkFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdb",
        ".xml",
        ".ilk",
        ".exp",
        ".lib",
        ".iobj",
        ".ipdb",
        ".dbg"
    };

    private static readonly HashSet<string> JunkFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "createdump.exe"
    };

    public static void Run(string directory, IAppLogger logger)
    {
        if (Debugger.IsAttached || string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        var deletedCount = 0;
        foreach (var filePath in Directory.EnumerateFiles(directory))
        {
            if (!IsJunkFile(filePath))
            {
                continue;
            }

            try
            {
                File.Delete(filePath);
                deletedCount++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.Warning(
                    $"Could not delete publish leftover '{Path.GetFileName(filePath)}': {ex.Message}",
                    LogTarget.File | LogTarget.Console);
            }
        }

        if (deletedCount > 0)
        {
            logger.Info(
                $"Publish leftover cleanup deleted {deletedCount} file(s).",
                LogTarget.File | LogTarget.Console);
        }
    }

    private static bool IsJunkFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (JunkFileNames.Contains(fileName))
        {
            return true;
        }

        return JunkFileExtensions.Contains(Path.GetExtension(filePath));
    }
}
