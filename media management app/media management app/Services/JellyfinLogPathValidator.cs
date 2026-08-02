using System.IO;

namespace media_management_app.Services;

/// <summary>
/// Resolves and validates a Jellyfin log folder or file path (shared by Settings Test and WARP hold).
/// </summary>
public static class JellyfinLogPathValidator
{
    public static bool TryResolveAndValidate(
        string? logPath,
        out string resolvedFilePath,
        out string errorMessage)
    {
        resolvedFilePath = string.Empty;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(logPath))
        {
            errorMessage = "Log path is empty.";
            return false;
        }

        var path = logPath.Trim();
        string? candidate;

        if (File.Exists(path))
        {
            candidate = path;
        }
        else if (Directory.Exists(path))
        {
            candidate = ResolveNewestLogFile(path);
            if (candidate is null)
            {
                errorMessage = "No .log files found in the folder.";
                return false;
            }
        }
        else
        {
            errorMessage = "Path does not exist (not a file or folder).";
            return false;
        }

        try
        {
            using var stream = new FileStream(
                candidate,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);

            // Soft sample: confirm we can read; do not reject on content (new/empty logs OK).
            if (stream.Length > 0)
            {
                var readFrom = Math.Max(0L, stream.Length - 4096L);
                stream.Seek(readFrom, SeekOrigin.Begin);
                using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
                _ = reader.ReadToEnd();
            }

            resolvedFilePath = candidate;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Cannot open log file: {ex.Message}";
            return false;
        }
    }

    private static string? ResolveNewestLogFile(string directory)
    {
        try
        {
            var preferred = Directory.EnumerateFiles(directory, "log*.log", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .FirstOrDefault();
            if (preferred is not null)
            {
                return preferred.FullName;
            }

            return Directory.EnumerateFiles(directory, "*.log", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .FirstOrDefault()
                ?.FullName;
        }
        catch
        {
            return null;
        }
    }
}
