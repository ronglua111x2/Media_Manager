using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services.Symlink;

public sealed class SymlinkSyncService : ISymlinkSyncService
{
    private readonly ISettingsService _settingsService;
    private readonly IHardlinkService _hardlinkService;
    private readonly ISymlinkService _symlinkService;
    private readonly IDatabaseService _databaseService;
    private readonly IAppLogger _logger;

    public SymlinkSyncService(
        ISettingsService settingsService,
        IHardlinkService hardlinkService,
        ISymlinkService symlinkService,
        IDatabaseService databaseService,
        IAppLogger logger)
    {
        _settingsService = settingsService;
        _hardlinkService = hardlinkService;
        _symlinkService = symlinkService;
        _databaseService = databaseService;
        _logger = logger;
    }

    public SymlinkSyncResult SyncItem(SourceItem item, string? linkedPath = null)
    {
        var result = new SymlinkSyncResult();
        var settings = _settingsService.Current.Symlink;

        if (!settings.Enabled)
        {
            result.SkippedCount++;
            return result;
        }

        linkedPath ??= item.LinkedPath;
        if (string.IsNullOrWhiteSpace(linkedPath) || !File.Exists(linkedPath))
        {
            result.SkippedCount++;
            result.Messages.Add($"{item.FileName}: linked path is missing or does not exist.");
            return result;
        }

        var linkedGroup = GetItemsSharingLinkedPath(linkedPath);
        if (linkedGroup.Count == 0)
        {
            linkedGroup = new List<SourceItem> { item };
        }

        var canonicalItem = ApplyTrackedMetadata(SelectCanonicalItem(linkedGroup));
        if (!TryValidateItem(canonicalItem, out var validationError))
        {
            result.SkippedCount++;
            result.Messages.Add($"{canonicalItem.FileName}: {validationError}");
            return result;
        }

        if (!TryResolveSymlinkPath(canonicalItem, out var symlinkPath, out var pathError))
        {
            result.ErrorCount++;
            result.Messages.Add($"{canonicalItem.FileName}: {pathError}");
            return result;
        }

        if (string.Equals(Path.GetFullPath(symlinkPath), Path.GetFullPath(linkedPath), StringComparison.OrdinalIgnoreCase))
        {
            result.SkippedCount++;
            result.Messages.Add($"{canonicalItem.FileName}: symlink path is on the same path as the hardlink. Skipped.");
            return result;
        }

        CleanupDuplicateSymlinks(linkedGroup, symlinkPath, result);

        if (File.Exists(symlinkPath) && _symlinkService.TryResolveSymlinkTarget(symlinkPath, out var existingTarget) &&
            string.Equals(Path.GetFullPath(existingTarget!), Path.GetFullPath(linkedPath), StringComparison.OrdinalIgnoreCase))
        {
            PersistSymlinkPathForGroup(linkedGroup, symlinkPath);
            result.SkippedCount++;
            return result;
        }

        if (File.Exists(symlinkPath))
        {
            if (!_symlinkService.TryRemoveSymlink(symlinkPath, out var removeError))
            {
                result.ErrorCount++;
                result.Messages.Add($"{canonicalItem.FileName}: could not replace existing symlink. {removeError}");
                return result;
            }

            result.RepairedCount++;
        }

        if (!_symlinkService.TryCreateFileSymlink(symlinkPath, linkedPath, out var createError))
        {
            result.ErrorCount++;
            result.Messages.Add($"{canonicalItem.FileName}: {createError}");
            return result;
        }

        PersistSymlinkPathForGroup(linkedGroup, symlinkPath);
        if (result.RepairedCount == 0)
        {
            result.CreatedCount++;
        }

        result.Messages.Add($"Symlinked {canonicalItem.FileName}");
        return result;
    }

    public SymlinkSyncResult RemoveItem(SourceItem item, string? linkedPath = null)
    {
        var result = new SymlinkSyncResult();
        var settings = _settingsService.Current.Symlink;
        if (!settings.Enabled)
        {
            return result;
        }

        linkedPath ??= item.LinkedPath;
        var linkedGroup = BuildRemovalGroup(item, linkedPath);

        var symlinkPaths = linkedGroup
            .Select(member => member.SymlinkPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (symlinkPaths.Count == 0)
        {
            var canonicalItem = ApplyTrackedMetadata(SelectCanonicalItem(linkedGroup));
            if (TryResolveSymlinkPath(canonicalItem, out var resolvedPath, out _))
            {
                symlinkPaths.Add(resolvedPath);
            }
        }

        foreach (var symlinkPath in symlinkPaths)
        {
            if (!string.IsNullOrWhiteSpace(symlinkPath) && File.Exists(symlinkPath))
            {
                if (_symlinkService.TryRemoveSymlink(symlinkPath, out var removeError))
                {
                    result.RemovedCount++;
                    CleanupEmptyFolders(Path.GetDirectoryName(symlinkPath), settings.UnifiedRoot);
                }
                else
                {
                    result.ErrorCount++;
                    result.Messages.Add($"{item.FileName}: {removeError}");
                }
            }
        }

        foreach (var member in linkedGroup)
        {
            ClearSymlinkPath(member);
        }

        return result;
    }

    public SymlinkSyncResult ReconcileAll()
    {
        var aggregate = new SymlinkSyncResult();
        var settings = _settingsService.Current.Symlink;

        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.UnifiedRoot))
        {
            aggregate.Messages.Add("Symlink sync is disabled or unified root is not configured.");
            return aggregate;
        }

        var linkedItems = _databaseService.GetSourceItems()
            .Where(item => item.State == ItemState.Linked)
            .ToList();

        var syncedLinkedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in linkedItems)
        {
            if (!string.IsNullOrWhiteSpace(item.LinkedPath) && File.Exists(item.LinkedPath))
            {
                var normalizedLinkedPath = Path.GetFullPath(item.LinkedPath);
                if (!syncedLinkedPaths.Add(normalizedLinkedPath))
                {
                    continue;
                }

                Merge(aggregate, SyncItem(item, item.LinkedPath));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(item.SymlinkPath))
            {
                Merge(aggregate, RemoveItem(item));
            }
        }

        _logger.Info($"Symlink startup reconcile finished. {aggregate.Summary}", LogTarget.All);
        return aggregate;
    }

    private List<SourceItem> GetItemsSharingLinkedPath(string linkedPath)
    {
        var normalizedLinkedPath = Path.GetFullPath(linkedPath);
        return _databaseService.GetSourceItems()
            .Where(item => item.State == ItemState.Linked)
            .Where(item => !string.IsNullOrWhiteSpace(item.LinkedPath))
            .Where(item => string.Equals(Path.GetFullPath(item.LinkedPath!), normalizedLinkedPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private List<SourceItem> BuildRemovalGroup(SourceItem item, string? linkedPath)
    {
        if (string.IsNullOrWhiteSpace(linkedPath))
        {
            return new List<SourceItem> { item };
        }

        var group = GetItemsSharingLinkedPath(linkedPath);
        if (!group.Any(member => member.Id == item.Id))
        {
            group.Add(item);
        }

        return group;
    }

    private static SourceItem SelectCanonicalItem(IReadOnlyList<SourceItem> items)
    {
        if (items.Count == 0)
        {
            throw new InvalidOperationException("Cannot select a canonical item from an empty linked-path group.");
        }

        return items
            .OrderByDescending(item => item.MatchAccepted)
            .ThenByDescending(item => !string.IsNullOrWhiteSpace(item.ProviderId))
            .ThenByDescending(item => !string.IsNullOrWhiteSpace(item.MatchedTitle))
            .ThenBy(item => item.IsExternalImport)
            .ThenBy(item => item.Id)
            .First();
    }

    private SourceItem ApplyTrackedMetadata(SourceItem item)
    {
        if (item.IsOrphanPackSpecial)
        {
            return item;
        }

        if (!int.TryParse(item.ProviderId, out var tmdbId))
        {
            return item;
        }

        if (item.MediaKind == MediaKind.Movie)
        {
            var movie = _databaseService.GetTrackedMovieByTmdbId(tmdbId);
            if (movie is null)
            {
                return item;
            }

            return CreateNamingItem(
                item,
                movie.Title,
                movie.ReleaseYear,
                movie.Title,
                MediaKind.Movie);
        }

        var show = _databaseService.GetTrackedShowByTmdbId(tmdbId);
        if (show is null)
        {
            return item;
        }

        return CreateNamingItem(
            item,
            show.Title,
            show.FirstAirYear,
            show.Title,
            MediaKind.TvEpisode);
    }

    private static SourceItem CreateNamingItem(
        SourceItem source,
        string title,
        int? year,
        string matchedTitle,
        MediaKind mediaKind)
    {
        return new SourceItem
        {
            MediaKind = mediaKind,
            FilePath = source.FilePath,
            FileName = source.FileName,
            ShowTitle = mediaKind == MediaKind.TvEpisode ? title : source.ShowTitle,
            MovieTitle = mediaKind == MediaKind.Movie ? title : source.MovieTitle,
            MovieYear = mediaKind == MediaKind.Movie ? year : source.MovieYear,
            MatchedTitle = matchedTitle,
            MatchedYear = year,
            Provider = source.Provider,
            ProviderId = source.ProviderId,
            SeasonNumber = source.SeasonNumber,
            EpisodeNumber = source.EpisodeNumber,
            MappedSeasonNumber = source.MappedSeasonNumber,
            MappedEpisodeNumber = source.MappedEpisodeNumber,
            ParserPattern = source.ParserPattern,
            EpisodeTitle = source.EpisodeTitle,
            MatchAccepted = source.MatchAccepted,
            RequiresManualReview = source.RequiresManualReview,
            IsOrphanPackSpecial = source.IsOrphanPackSpecial,
            AutoTorrentPackOwnerSeasonNumber = source.AutoTorrentPackOwnerSeasonNumber
        };
    }

    private void CleanupDuplicateSymlinks(
        IReadOnlyList<SourceItem> linkedGroup,
        string canonicalSymlinkPath,
        SymlinkSyncResult result)
    {
        var settings = _settingsService.Current.Symlink;
        foreach (var member in linkedGroup)
        {
            if (string.IsNullOrWhiteSpace(member.SymlinkPath) ||
                string.Equals(member.SymlinkPath, canonicalSymlinkPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (File.Exists(member.SymlinkPath) &&
                _symlinkService.TryRemoveSymlink(member.SymlinkPath, out _))
            {
                result.RemovedCount++;
                CleanupEmptyFolders(Path.GetDirectoryName(member.SymlinkPath), settings.UnifiedRoot);
                _logger.Info(
                    $"Removed duplicate symlink for linked path group: {member.SymlinkPath}",
                    LogTarget.All);
            }

            ClearSymlinkPath(member);
        }
    }

    private bool TryValidateItem(SourceItem item, out string? errorMessage)
    {
        errorMessage = null;

        if (item.MediaKind == MediaKind.TvEpisode &&
            (string.IsNullOrWhiteSpace(item.MatchedTitle) ||
             string.IsNullOrWhiteSpace(item.ProviderId) ||
             item.RequiresManualReview ||
             !item.MatchAccepted))
        {
            errorMessage = "TV item does not have an accepted metadata identity.";
            return false;
        }

        if (item.MediaKind == MediaKind.TvEpisode &&
            item.ParserPattern == ParserPattern.AnimeAbsolute &&
            (item.MappedSeasonNumber is null || item.MappedEpisodeNumber is null))
        {
            errorMessage = "Anime absolute episode does not have a TMDb season mapping.";
            return false;
        }

        if (item.MediaKind is not (MediaKind.TvEpisode or MediaKind.Movie))
        {
            errorMessage = $"Unsupported media kind: {item.MediaKind}";
            return false;
        }

        return true;
    }

    private bool TryResolveSymlinkPath(SourceItem item, out string symlinkPath, out string? errorMessage)
    {
        symlinkPath = string.Empty;
        errorMessage = null;

        var settings = _settingsService.Current.Symlink;
        if (string.IsNullOrWhiteSpace(settings.UnifiedRoot))
        {
            errorMessage = "Unified Jellyfin root is not configured.";
            return false;
        }

        var mediaFolder = item.MediaKind switch
        {
            MediaKind.TvEpisode => AppConstants.ShowsFolderName,
            MediaKind.Movie => AppConstants.MoviesFolderName,
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(mediaFolder))
        {
            errorMessage = $"Unsupported media kind for symlink output: {item.MediaKind}";
            return false;
        }

        var mediaRoot = Path.Combine(settings.UnifiedRoot, mediaFolder);
        symlinkPath = _hardlinkService.BuildOutputPath(item, mediaRoot);
        return true;
    }

    private void PersistSymlinkPathForGroup(IReadOnlyList<SourceItem> linkedGroup, string symlinkPath)
    {
        foreach (var member in linkedGroup)
        {
            PersistSymlinkPath(member, symlinkPath);
        }
    }

    private void PersistSymlinkPath(SourceItem item, string symlinkPath)
    {
        if (string.Equals(item.SymlinkPath, symlinkPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        item.SymlinkPath = symlinkPath;
        _databaseService.UpdateSourceItem(item);
    }

    private void ClearSymlinkPath(SourceItem item)
    {
        if (string.IsNullOrWhiteSpace(item.SymlinkPath))
        {
            return;
        }

        item.SymlinkPath = null;
        _databaseService.UpdateSourceItem(item);
    }

    private void CleanupEmptyFolders(string? startDirectory, string outputRoot)
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
            _logger.Info($"Removed empty symlink folder: {current}", LogTarget.File | LogTarget.Console);

            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                break;
            }

            current = parent.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static bool IsPathInsideRoot(string path, string root)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void Merge(SymlinkSyncResult aggregate, SymlinkSyncResult itemResult)
    {
        aggregate.CreatedCount += itemResult.CreatedCount;
        aggregate.RepairedCount += itemResult.RepairedCount;
        aggregate.RemovedCount += itemResult.RemovedCount;
        aggregate.SkippedCount += itemResult.SkippedCount;
        aggregate.ErrorCount += itemResult.ErrorCount;
        aggregate.Messages.AddRange(itemResult.Messages);
    }
}
