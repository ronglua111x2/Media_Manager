using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class SourceReconciliationService : ISourceReconciliationService
{
    private readonly IDatabaseService _databaseService;
    private readonly IHardlinkService _hardlinkService;
    private readonly IAppLogger _logger;

    public SourceReconciliationService(
        IDatabaseService databaseService,
        IHardlinkService hardlinkService,
        IAppLogger logger)
    {
        _databaseService = databaseService;
        _hardlinkService = hardlinkService;
        _logger = logger;
    }

    public SourceReconciliationResult ReconcileMissingSourceItems(IEnumerable<string> sourceFolders, IEnumerable<string> seenFilePaths)
    {
        var result = new SourceReconciliationResult();
        var configuredRoots = sourceFolders
            .Select(NormalizePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var availableDrives = configuredRoots
            .Select(Path.GetPathRoot)
            .Where(root => !string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            .Cast<string>()
            .Select(NormalizePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = seenFilePaths
            .Select(NormalizePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (availableDrives.Count == 0)
        {
            _logger.Warning("Skipping missing-source reconciliation because no configured source drives are currently available.", LogTarget.All);
            return result;
        }

        foreach (var item in _databaseService.GetSourceItems())
        {
            if (!IsConfiguredSourceRootAvailable(item, configuredRoots, availableDrives) ||
                seen.Contains(NormalizePath(item.FilePath)) ||
                File.Exists(item.FilePath))
            {
                continue;
            }

            _logger.Warning($"Missing source detected: {item.FilePath}", LogTarget.All);
            ReconcileMissingItem(item, result);
        }

        _logger.Info(
            $"Missing-source reconciliation complete. MarkedDeleted={result.MarkedDeletedCount}, PurgedStale={result.PurgedStaleCount}, CleanupFailures={result.CleanupFailureCount}.",
            LogTarget.All);
        return result;
    }

    private void ReconcileMissingItem(SourceItem item, SourceReconciliationResult result)
    {
        if (string.IsNullOrWhiteSpace(item.LinkedPath))
        {
            PurgeStaleRecord(item, result, "source and linked path are both absent");
            return;
        }

        if (File.Exists(item.LinkedPath))
        {
            if (item.State != ItemState.Deleted)
            {
                item.State = ItemState.Deleted;
                item.Notes = "Source file is missing; linked file still exists and needs cleanup.";
                item.LastSeenUtc = DateTime.UtcNow;
                _databaseService.UpdateSourceItem(item);
                result.MarkedDeletedCount++;
            }

            _logger.Warning($"Linked path still exists, marked item for cleanup: {item.LinkedPath}", LogTarget.All);
            return;
        }

        _logger.Info($"Linked path already gone; pruning folders before purging record: {item.LinkedPath}", LogTarget.All);
        if (_hardlinkService.RemoveHardLink(item, out _, out var errorMessage))
        {
            PurgeStaleRecord(item, result, "source and linked file are both missing");
            return;
        }

        item.State = ItemState.Error;
        item.Notes = $"Stale cleanup failed: {errorMessage}";
        item.LastSeenUtc = DateTime.UtcNow;
        _databaseService.UpdateSourceItem(item);
        result.CleanupFailureCount++;
        _logger.Error($"Could not cleanup stale linked path before purging item: {item.LinkedPath}. {errorMessage}", targets: LogTarget.All);
    }

    private void PurgeStaleRecord(SourceItem item, SourceReconciliationResult result, string reason)
    {
        var deleted = _databaseService.DeleteSourceItem(item.Id);
        if (deleted > 0)
        {
            result.PurgedStaleCount += deleted;
            _logger.Info($"Purged stale source item id={item.Id}. Reason: {reason}. Path={item.FilePath}", LogTarget.All);
        }
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsConfiguredSourceRootAvailable(
        SourceItem item,
        HashSet<string> configuredRoots,
        HashSet<string> availableDrives)
    {
        var sourceRoot = NormalizePath(item.SourceRootFolder);
        if (!configuredRoots.Contains(sourceRoot))
        {
            return false;
        }

        if (Directory.Exists(sourceRoot))
        {
            return true;
        }

        var driveRoot = Path.GetPathRoot(sourceRoot);
        return !string.IsNullOrWhiteSpace(driveRoot) && availableDrives.Contains(NormalizePath(driveRoot));
    }
}
