using System.Collections.Concurrent;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class PackLinkCoordinatorService : IPackLinkCoordinatorService
{
    private static readonly TimeSpan AutoCooldown = TimeSpan.FromMinutes(30);

    private readonly IDatabaseService _databaseService;
    private readonly IQbittorrentClient _qbittorrentClient;
    private readonly IAppLogger _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    public PackLinkCoordinatorService(
        IDatabaseService databaseService,
        IQbittorrentClient qbittorrentClient,
        IAppLogger logger)
    {
        _databaseService = databaseService;
        _qbittorrentClient = qbittorrentClient;
        _logger = logger;
    }

    public event EventHandler<PackReconcileResult>? PackReconciled;

    public async Task<PackReconcileResult> ReconcilePackAsync(
        long showId,
        int ownerSeasonNumber,
        PackLinkTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        var show = _databaseService.GetTrackedShow(showId);
        var season = _databaseService.GetTrackedSeasons(showId)
            .FirstOrDefault(item => item.SeasonNumber == ownerSeasonNumber);
        if (show is null || season is null)
        {
            return Skipped("Tracked pack owner season was not found.");
        }

        if (!IsPackOwner(season))
        {
            return Skipped($"Season S{season.SeasonNumber:00} is covered by pack owner S{season.SelectedPackOwnerSeasonNumber:00}.");
        }

        if (string.IsNullOrWhiteSpace(season.PackTorrentHash))
        {
            return Skipped("Pack torrent hash is not mapped yet.");
        }

        var torrent = (await _qbittorrentClient.GetTorrentsAsync(cancellationToken))
            .FirstOrDefault(item => string.Equals(item.Hash, season.PackTorrentHash, StringComparison.OrdinalIgnoreCase));
        if (torrent is null)
        {
            return Skipped("Pack torrent is not present in qBittorrent.");
        }

        if (!torrent.IsComplete)
        {
            return Skipped("Pack torrent is not complete yet.");
        }

        if (trigger == PackLinkTrigger.AutoComplete &&
            ShouldSkipAutoCooldown(season, torrent.Hash))
        {
            return Skipped("Pack was already inspected for this torrent.");
        }

        return await RunReconcileCoreAsync(show, season, torrent, trigger, cancellationToken);
    }

    public async Task TryAutoReconcilePackAsync(
        TrackedShow show,
        TrackedSeason season,
        AddedTorrentResult torrent,
        double previousProgress,
        TorrentReconciliationResult reconcileResult,
        CancellationToken cancellationToken = default)
    {
        if (!IsPackOwner(season))
        {
            return;
        }

        if (!torrent.IsComplete || previousProgress >= 0.999)
        {
            return;
        }

        await ReconcilePackAsync(show.Id, season.SeasonNumber, PackLinkTrigger.AutoComplete, cancellationToken);
    }

    private async Task<PackReconcileResult> RunReconcileCoreAsync(
        TrackedShow show,
        TrackedSeason season,
        AddedTorrentResult torrent,
        PackLinkTrigger trigger,
        CancellationToken cancellationToken)
    {
        var lockKey = $"{show.Id}:{season.SeasonNumber}:{torrent.Hash}";
        if (!_inFlight.TryAdd(lockKey, 0))
        {
            return Skipped("Pack inspect is already running for this torrent.");
        }

        var gate = _locks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var episodes = _databaseService.GetTrackedEpisodes(show.Id);
            var seasons = _databaseService.GetTrackedSeasons(show.Id);
            var torrentFiles = await _qbittorrentClient.GetTorrentFilesAsync(torrent.Hash, cancellationToken);
            var files = torrentFiles
                .Where(file => file.IsVideoFile && file.IsComplete)
                .Select(file => (file.Name, Path.GetFileName(file.Name)))
                .ToList();

            LogInspectInputs(show, season, torrent, trigger, torrentFiles, files, seasons);

            var inventory = PackTorrentInventoryAnalyzer.Analyze(
                files,
                episodes,
                seasons,
                mode: PackAnalyzeMode.Inspect,
                logger: _logger);

            _databaseService.UpdateTrackedSeasonPackInspection(show.Id, season.SeasonNumber, inventory);
            _databaseService.UpdateTrackedSeasonLastPackLink(show.Id, season.SeasonNumber, torrent.Hash, DateTime.UtcNow);
            UpdatePackCartOrders(show.Id, season.SeasonNumber, inventory);

            var result = new PackReconcileResult
            {
                Ran = true,
                Inventory = inventory
            };

            _logger.Info(
                $"Pack inspect ({trigger}) for '{show.DisplayTitle}' S{season.SeasonNumber:00}: {result.Summary}",
                LogTarget.All);
            PackReconciled?.Invoke(this, result);
            return result;
        }
        finally
        {
            gate.Release();
            _inFlight.TryRemove(lockKey, out _);
        }
    }

    private void LogInspectInputs(
        TrackedShow show,
        TrackedSeason season,
        AddedTorrentResult torrent,
        PackLinkTrigger trigger,
        IReadOnlyList<TorrentContentFile> torrentFiles,
        IReadOnlyList<(string Name, string FileName)> files,
        IReadOnlyList<TrackedSeason> seasons)
    {
        var trackedSeasons = string.Join(
            ",",
            seasons
                .Where(item => item.SeasonNumber > 0)
                .Select(item => $"S{item.SeasonNumber:00}"));
        _logger.Info(
            $"Pack inspect inputs ({trigger}): show='{show.DisplayTitle}' owner=S{season.SeasonNumber:00} torrent='{torrent.Name}' hash={torrent.Hash} qbitFiles={torrentFiles.Count} videoComplete={files.Count} trackedSeasons=[{trackedSeasons}]",
            LogTarget.File);

        foreach (var file in torrentFiles.Where(item => !item.IsVideoFile || !item.IsComplete))
        {
            _logger.Info(
                $"Pack inspect skipped qBittorrent file '{file.Name}' video={file.IsVideoFile} complete={file.IsComplete} progress={file.Progress:0.###}",
                LogTarget.File);
        }
    }

    private void UpdatePackCartOrders(long showId, int ownerSeasonNumber, PackTorrentInventory inventory)
    {
        var detail = inventory.BuildInspectionDetailText();
        foreach (var order in _databaseService.GetTorrentCartOrders(MediaKind.TvEpisode, showId))
        {
            if (order.EpisodeId is not null || order.SeasonNumber != ownerSeasonNumber)
            {
                continue;
            }

            order.StatusDetail = detail;
            if (order.Status is TorrentOrderStatus.Downloading or TorrentOrderStatus.AddedToClient)
            {
                order.Status = TorrentOrderStatus.Completed;
            }

            _databaseService.UpsertTorrentCartOrder(order);
        }
    }

    private bool ShouldSkipAutoCooldown(TrackedSeason season, string torrentHash)
    {
        if (!string.Equals(season.LastPackLinkTorrentHash, torrentHash, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return season.LastPackLinkUtc is DateTime lastLink &&
               DateTime.UtcNow - lastLink < AutoCooldown;
    }

    private static bool IsPackOwner(TrackedSeason season) =>
        season.SelectedPackOwnerSeasonNumber is null ||
        season.SelectedPackOwnerSeasonNumber == season.SeasonNumber;

    private static PackReconcileResult Skipped(string reason) =>
        new()
        {
            Skipped = true,
            SkipReason = reason
        };
}
