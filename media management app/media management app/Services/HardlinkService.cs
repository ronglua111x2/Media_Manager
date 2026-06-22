using System.Runtime.InteropServices;
using System.ComponentModel;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services.Events;

namespace media_management_app.Services;

public sealed class HardlinkService : IHardlinkService
{
    private readonly IAppLogger _logger;
    private readonly ILibraryPathResolver _libraryPathResolver;
    private readonly ISettingsService _settingsService;
    private readonly ILibraryLinkEventHub _eventHub;

    public HardlinkService(
        IAppLogger logger,
        ILibraryPathResolver libraryPathResolver,
        ISettingsService settingsService,
        ILibraryLinkEventHub eventHub)
    {
        _logger = logger;
        _libraryPathResolver = libraryPathResolver;
        _settingsService = settingsService;
        _eventHub = eventHub;
    }

    public string BuildOutputPath(SourceItem item, string outputRoot)
    {
        if (item.MediaKind == MediaKind.Movie)
        {
            var movieFolderName = BuildMovieFolderName(item);
            var extension = Path.GetExtension(item.FilePath);
            return Path.Combine(outputRoot, movieFolderName, $"{movieFolderName}{extension}");
        }

        var showName = BuildSeriesFolderName(item);
        var seasonNumber = item.MappedSeasonNumber ?? item.SeasonNumber.GetValueOrDefault();
        var seasonFolder = $"Season {seasonNumber:00}";
        if (item.IsOrphanPackSpecial)
        {
            var orphanFileName = SanitizeFileName(item.FileName);
            return Path.Combine(outputRoot, showName, AppConstants.OrphanExtrasFolderName, orphanFileName);
        }

        var episodeNumber = item.MappedEpisodeNumber ?? item.EpisodeNumber.GetValueOrDefault();
        var fileTitle = Sanitize(item.MatchedTitle ?? item.ShowTitle ?? "Unknown Show");
        var episodeSuffix = seasonNumber == AppConstants.SpecialsSeasonNumber &&
                            !string.IsNullOrWhiteSpace(item.EpisodeTitle)
            ? $" - {Sanitize(item.EpisodeTitle)}"
            : string.Empty;
        var fileName = $"{fileTitle} - S{seasonNumber:00}E{episodeNumber:00}{episodeSuffix}{Path.GetExtension(item.FilePath)}";
        return Path.Combine(outputRoot, showName, seasonFolder, fileName);
    }

    public bool CreateHardLink(SourceItem item, out string? createdPath, out string? errorMessage)
    {
        createdPath = null;
        errorMessage = null;

        if (item.MediaKind == MediaKind.TvEpisode &&
            (string.IsNullOrWhiteSpace(item.MatchedTitle) ||
             string.IsNullOrWhiteSpace(item.ProviderId) ||
             item.RequiresManualReview ||
             !item.MatchAccepted))
        {
            errorMessage = "TV item does not have an accepted metadata identity. Refusing to create an ambiguous Jellyfin folder.";
            _logger.Warning($"Hardlink blocked for unresolved TV identity: {item.FilePath}", LogTarget.All);
            return false;
        }

        if (item.MediaKind == MediaKind.TvEpisode &&
            item.ParserPattern == ParserPattern.AnimeAbsolute &&
            (item.MappedSeasonNumber is null || item.MappedEpisodeNumber is null))
        {
            errorMessage = "Anime absolute episode does not have a TMDb season mapping. Refusing to create Season 01 absolute fallback.";
            _logger.Warning($"Hardlink blocked for unmapped anime absolute episode: {item.FilePath}", LogTarget.All);
            return false;
        }

        if (!_libraryPathResolver.TryResolveMediaRoot(item, out var mediaRoot, out errorMessage))
        {
            _logger.Warning($"Hardlink blocked because media root could not be resolved: {errorMessage}", LogTarget.All);
            return false;
        }

        var targetPath = BuildOutputPath(item, mediaRoot);
        if (!IsSameVolume(item.FilePath, targetPath))
        {
            errorMessage = $"Source and target are on different drives. Source={Path.GetPathRoot(item.FilePath)}, Target={Path.GetPathRoot(targetPath)}";
            _logger.Error(errorMessage, targets: LogTarget.All);
            return false;
        }

        if (File.Exists(targetPath))
        {
            errorMessage = "Output already exists.";
            _logger.Warning($"Hardlink skipped because output exists: {targetPath}", LogTarget.All);
            return false;
        }

        if (!File.Exists(item.FilePath))
        {
            errorMessage = $"Source file does not exist: {item.FilePath}";
            _logger.Error($"Hardlink failed before native call: {errorMessage}", targets: LogTarget.All);
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        _logger.Info($"Creating hardlink from {item.FilePath} to {targetPath}", LogTarget.File | LogTarget.Console);
        var nativeTargetPath = ToExtendedLengthPath(targetPath);
        var nativeSourcePath = ToExtendedLengthPath(item.FilePath);
        var ok = CreateHardLinkNative(nativeTargetPath, nativeSourcePath, IntPtr.Zero);
        if (!ok)
        {
            var errorCode = Marshal.GetLastWin32Error();
            errorMessage = $"{new Win32Exception(errorCode).Message} (Win32 error {errorCode})";
            _logger.Error($"Hardlink failed: {errorMessage}", targets: LogTarget.All);
            return false;
        }

        createdPath = targetPath;
        _logger.Info($"Created hardlink: {targetPath}", LogTarget.All);
        return true;
    }

    public bool RemoveHardLink(SourceItem item, out string? removedPath, out string? errorMessage)
    {
        removedPath = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(item.LinkedPath))
        {
            errorMessage = "Item does not have a linked path.";
            _logger.Warning($"Remove hardlink skipped because item has no linked path: {item.FilePath}", LogTarget.Ui | LogTarget.Console);
            return false;
        }

        if (string.Equals(Path.GetFullPath(item.LinkedPath), Path.GetFullPath(item.FilePath), StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "Linked path matches the source path. Refusing to delete.";
            _logger.Error($"Refusing to delete source path while removing hardlink: {item.FilePath}", targets: LogTarget.All);
            return false;
        }

        removedPath = item.LinkedPath;
        if (!_libraryPathResolver.TryResolveGeneratedLibraryRoot(item, out var libraryRoot, out errorMessage))
        {
            _logger.Warning($"Could not resolve generated library root while removing hardlink: {errorMessage}", LogTarget.All);
            return false;
        }

        var cleanupRoot = ResolveCleanupRoot(item.LinkedPath, libraryRoot);
        if (cleanupRoot is null)
        {
            errorMessage = "Linked path is outside the configured output library. Refusing to delete.";
            _logger.Error($"Refusing to delete linked path outside output root. LinkedPath={item.LinkedPath}; LibraryRoot={libraryRoot}", targets: LogTarget.All);
            return false;
        }

        var linkedDirectory = Path.GetDirectoryName(item.LinkedPath);
        if (!File.Exists(item.LinkedPath))
        {
            _logger.Warning($"Linked path no longer exists, clearing state only: {item.LinkedPath}", LogTarget.All);
            CleanupEmptyLibraryFolders(linkedDirectory, cleanupRoot);
            _eventHub.PublishHardlinkRemoved(item, removedPath);
            return true;
        }

        try
        {
            File.Delete(item.LinkedPath);
            _logger.Info($"Removed hardlink path: {item.LinkedPath}", LogTarget.All);
            CleanupEmptyLibraryFolders(linkedDirectory, cleanupRoot);
            _eventHub.PublishHardlinkRemoved(item, removedPath);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            _logger.Error($"Could not remove hardlink path: {item.LinkedPath}", ex, LogTarget.All);
            return false;
        }
    }

    private void CleanupEmptyLibraryFolders(string? startDirectory, string outputRoot)
    {
        if (string.IsNullOrWhiteSpace(startDirectory) || !Directory.Exists(startDirectory))
        {
            return;
        }

        var root = Path.GetFullPath(outputRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = Path.GetFullPath(startDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        while (!string.Equals(current, root, StringComparison.OrdinalIgnoreCase) &&
               IsPathInsideRoot(current, root) &&
               Directory.Exists(current) &&
               !Directory.EnumerateFileSystemEntries(current).Any())
        {
            Directory.Delete(current);
            _logger.Info($"Removed empty library folder: {current}", LogTarget.File | LogTarget.Ui | LogTarget.Console);

            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                break;
            }

            current = parent.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private string? ResolveCleanupRoot(string linkedPath, string generatedLibraryRoot)
    {
        if (IsPathInsideRoot(linkedPath, generatedLibraryRoot))
        {
            return generatedLibraryRoot;
        }

        var legacyRoot = _settingsService.Current.OutputLibraryFolder;
        if (!string.IsNullOrWhiteSpace(legacyRoot) && IsPathInsideRoot(linkedPath, legacyRoot))
        {
            return legacyRoot;
        }

        return null;
    }

    private static string Sanitize(string value)
    {
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(ch, '_');
        }

        return string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string SanitizeFileName(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        return $"{Sanitize(baseName)}{extension}";
    }

    private static string BuildMovieFolderName(SourceItem item)
    {
        var title = Sanitize(item.MatchedTitle ?? item.MovieTitle ?? item.ShowTitle ?? "Unknown Movie");
        var year = item.MovieYear ?? item.MatchedYear;
        var yearSuffix = year is null ? string.Empty : $" ({year})";
        var providerSuffix = !string.IsNullOrWhiteSpace(item.ProviderId)
            ? $" [{(item.Provider ?? "tmdb").ToLowerInvariant()}id-{item.ProviderId}]"
            : string.Empty;
        return $"{title}{yearSuffix}{providerSuffix}";
    }

    private static string BuildSeriesFolderName(SourceItem item)
    {
        var title = Sanitize(item.MatchedTitle ?? item.ShowTitle ?? "Unknown Show");
        var yearSuffix = item.MatchedYear is null ? string.Empty : $" ({item.MatchedYear})";
        var providerSuffix = !string.IsNullOrWhiteSpace(item.ProviderId)
            ? $" [{(item.Provider ?? "tmdb").ToLowerInvariant()}id-{item.ProviderId}]"
            : string.Empty;
        return $"{title}{yearSuffix}{providerSuffix}";
    }

    private static bool IsPathInsideRoot(string path, string root)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameVolume(string sourcePath, string targetPath)
    {
        var sourceRoot = Path.GetPathRoot(Path.GetFullPath(sourcePath));
        var targetRoot = Path.GetPathRoot(Path.GetFullPath(targetPath));
        return !string.IsNullOrWhiteSpace(sourceRoot) &&
               !string.IsNullOrWhiteSpace(targetRoot) &&
               string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string ToExtendedLengthPath(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return path;
        }

        var fullPath = Path.GetFullPath(path);
        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return $@"\\?\UNC\{fullPath[2..]}";
        }

        return $@"\\?\{fullPath}";
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateHardLinkNative(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
}
