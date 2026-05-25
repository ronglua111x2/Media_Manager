using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;
using media_management_app.ViewModels.Filters;

namespace media_management_app.ViewModels;

public partial class InboxViewModel : ViewModelBase
{
    private const double MinAcceptedMatchConfidence = 75;

    private readonly ISettingsService _settingsService;
    private readonly IDatabaseService _databaseService;
    private readonly IScannerService _scannerService;
    private readonly IHardlinkService _hardlinkService;
    private readonly ISourceReconciliationService _sourceReconciliationService;
    private readonly IMetadataProvider _metadataProvider;
    private readonly IOperationProgressService _progressService;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _operationQueue = new(1, 1);
    private readonly List<SourceItem> _allItems = [];
    private readonly List<ISourceItemFilter> _filters;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private SourceItem? selectedItem;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private FilterOption<ItemState>? selectedStateFilter;

    [ObservableProperty]
    private FilterOption<MediaKind>? selectedKindFilter;

    [ObservableProperty]
    private string searchText = string.Empty;

    public InboxViewModel(
        ISettingsService settingsService,
        IDatabaseService databaseService,
        IScannerService scannerService,
        IHardlinkService hardlinkService,
        ISourceReconciliationService sourceReconciliationService,
        IMetadataProvider metadataProvider,
        IOperationProgressService progressService,
        IAppLogger logger)
    {
        _settingsService = settingsService;
        _databaseService = databaseService;
        _scannerService = scannerService;
        _hardlinkService = hardlinkService;
        _sourceReconciliationService = sourceReconciliationService;
        _metadataProvider = metadataProvider;
        _progressService = progressService;
        _logger = logger;
        Items = new ObservableCollection<SourceItem>();
        StateFilterOptions = BuildStateFilterOptions();
        KindFilterOptions = BuildKindFilterOptions();
        SelectedStateFilter = StateFilterOptions[0];
        SelectedKindFilter = KindFilterOptions[0];
        _filters =
        [
            new SourceItemStateFilter(() => SelectedStateFilter?.Value),
            new SourceItemKindFilter(() => SelectedKindFilter?.Value),
            new SourceItemTextFilter(() => SearchText)
        ];
        ReloadPersistedItems();
    }

    public ObservableCollection<SourceItem> Items { get; }

    public ObservableCollection<SourceItem> SelectedItems { get; } = [];

    public IReadOnlyList<FilterOption<ItemState>> StateFilterOptions { get; }

    public IReadOnlyList<FilterOption<MediaKind>> KindFilterOptions { get; }

    [RelayCommand]
    private async Task Scan()
    {
        IsBusy = true;
        try
        {
            _progressService.Start("Scanning source folders...", 1);
            var scanned = _scannerService.Scan(_settingsService.Current.SourceFolders);
            _progressService.Report(1, $"Found {scanned.Count} video item(s)");
            await ResolveTvIdentityAsync(scanned);
            _databaseService.UpsertSourceItems(scanned);
            var reconciliation = _sourceReconciliationService.ReconcileMissingSourceItems(_settingsService.Current.SourceFolders, scanned.Select(item => item.FilePath));
            ReloadPersistedItems();
            StatusMessage = $"Scanned {scanned.Count} video item(s). Marked deleted: {reconciliation.MarkedDeletedCount}. Purged stale: {reconciliation.PurgedStaleCount}. Cleanup failures: {reconciliation.CleanupFailureCount}.";
            _progressService.Finish(StatusMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void UpdateSelectedItems(IEnumerable<SourceItem> selectedItems)
    {
        SelectedItems.Clear();
        foreach (var item in selectedItems)
        {
            SelectedItems.Add(item);
        }
    }

    partial void OnSelectedStateFilterChanged(FilterOption<ItemState>? value)
    {
        ApplyFilters();
    }

    partial void OnSelectedKindFilterChanged(FilterOption<MediaKind>? value)
    {
        ApplyFilters();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilters();
    }

    [RelayCommand]
    private async Task CreateHardlinks()
    {
        var queuedItems = GetQueuedSelection();
        if (queuedItems.Count == 0)
        {
            StatusMessage = "Select one or more items first.";
            _logger.Warning("Create hardlink requested without selected items", LogTarget.Ui | LogTarget.Console);
            return;
        }

        await _operationQueue.WaitAsync();
        IsBusy = true;
        try
        {
            var successCount = 0;
            var failureCount = 0;
            var processedCount = 0;
            StatusMessage = $"Creating hardlinks for {queuedItems.Count} selected item(s)...";
            _progressService.Start(StatusMessage, queuedItems.Count);

            foreach (var item in queuedItems)
            {
                await Task.Yield();
                processedCount++;
                _progressService.Report(processedCount, $"Creating hardlinks: {item.DisplayTitle}");
                if (item.State is ItemState.Deleted or ItemState.Ignored || !CanLink(item))
                {
                    failureCount++;
                    item.Notes = item.State switch
                    {
                        ItemState.Deleted => "Source file is deleted and cannot be linked.",
                        ItemState.Ignored => "Ignored item cannot be linked.",
                        _ => "Item needs accepted metadata identity before linking."
                    };
                    _databaseService.UpdateSourceItem(item);
                    _logger.Warning($"Skipped item before linking: {item.FilePath}. {item.Notes}", LogTarget.Ui | LogTarget.Console);
                    continue;
                }

                if (_hardlinkService.CreateHardLink(item, out var createdPath, out var errorMessage))
                {
                    successCount++;
                    item.State = ItemState.Linked;
                    item.LinkedPath = createdPath;
                    item.Notes = null;
                    _databaseService.UpdateSourceItem(item);
                    continue;
                }

                failureCount++;
                item.State = ItemState.Error;
                item.Notes = errorMessage;
                _databaseService.UpdateSourceItem(item);
            }

            ReloadPersistedItems();
            StatusMessage = $"Hardlink queue finished. Created: {successCount}. Failed/skipped: {failureCount}.";
            _progressService.Finish(StatusMessage);
        }
        finally
        {
            IsBusy = false;
            _operationQueue.Release();
        }
    }

    [RelayCommand]
    private async Task RemoveHardlinks()
    {
        var queuedItems = GetQueuedSelection();
        if (queuedItems.Count == 0)
        {
            StatusMessage = "Select one or more items first.";
            _logger.Warning("Remove hardlink requested without selected items", LogTarget.Ui | LogTarget.Console);
            return;
        }

        await _operationQueue.WaitAsync();
        IsBusy = true;
        try
        {
            var successCount = 0;
            var failureCount = 0;
            var purgedCount = 0;
            var processedCount = 0;
            StatusMessage = $"Removing hardlinks for {queuedItems.Count} selected item(s)...";
            _progressService.Start(StatusMessage, queuedItems.Count);

            foreach (var item in queuedItems)
            {
                await Task.Yield();
                processedCount++;
                _progressService.Report(processedCount, $"Removing hardlinks: {item.DisplayTitle}");

                if (string.IsNullOrWhiteSpace(item.LinkedPath))
                {
                    if (!File.Exists(item.FilePath))
                    {
                        purgedCount += _databaseService.DeleteSourceItem(item.Id);
                        _logger.Info($"Removed stale table entry because both source and linked path are absent: {item.FilePath}", LogTarget.All);
                        continue;
                    }

                    successCount++;
                    item.State = CanLink(item) ? ItemState.Parsed : ItemState.NeedsReview;
                    item.Notes = "No linked path is currently recorded. Nothing to remove.";
                    _databaseService.UpdateSourceItem(item);
                    continue;
                }

                if (_hardlinkService.RemoveHardLink(item, out _, out var errorMessage))
                {
                    if (!File.Exists(item.FilePath))
                    {
                        purgedCount += _databaseService.DeleteSourceItem(item.Id);
                        _logger.Info($"Removed hardlink and purged table entry because source is missing: {item.FilePath}", LogTarget.All);
                        continue;
                    }

                    successCount++;
                    item.LinkedPath = null;
                    item.State = CanLink(item) ? ItemState.Parsed : ItemState.NeedsReview;
                    item.Notes = null;
                    _databaseService.UpdateSourceItem(item);
                    continue;
                }

                failureCount++;
                item.State = ItemState.Error;
                item.Notes = errorMessage;
                _databaseService.UpdateSourceItem(item);
            }

            ReloadPersistedItems();
            StatusMessage = $"Remove queue finished. Removed/cleared: {successCount}. Purged stale rows: {purgedCount}. Failed/skipped: {failureCount}.";
            _progressService.Finish(StatusMessage);
        }
        finally
        {
            IsBusy = false;
            _operationQueue.Release();
        }
    }

    [RelayCommand]
    private async Task AcceptSuggestedMatches()
    {
        var queuedItems = GetQueuedSelection();
        if (queuedItems.Count == 0)
        {
            StatusMessage = "Select one or more items first.";
            return;
        }

        await _operationQueue.WaitAsync();
        IsBusy = true;
        try
        {
            var acceptedCount = 0;
            var skippedCount = 0;
            var processedCount = 0;
            var savedMappings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _progressService.Start($"Accepting {queuedItems.Count} suggested match(es)...", queuedItems.Count);
            foreach (var item in queuedItems)
            {
                await Task.Yield();
                processedCount++;
                _progressService.Report(processedCount, $"Accepting match: {item.DisplayTitle}");
                if (item.State == ItemState.Ignored ||
                    item.MediaKind != MediaKind.TvEpisode ||
                    string.IsNullOrWhiteSpace(item.ShowTitle) ||
                    string.IsNullOrWhiteSpace(item.MatchedTitle) ||
                    string.IsNullOrWhiteSpace(item.ProviderId))
                {
                    skippedCount++;
                    continue;
                }

                if (item.MatchConfidence is null || item.MatchConfidence < MinAcceptedMatchConfidence)
                {
                    skippedCount++;
                    item.MatchAccepted = false;
                    item.RequiresManualReview = true;
                    item.State = ItemState.NeedsReview;
                    item.Notes = $"Low-confidence suggestion ({item.MatchConfidence:0}) was not accepted automatically. Use a manual match once candidate selection is available.";
                    _databaseService.UpdateSourceItem(item);
                    _logger.Warning($"Skipped accepting low-confidence match for {item.FilePath}: {item.MatchedTitle} ({item.MatchedYear}) [{item.ProviderId}] confidence={item.MatchConfidence:0}", LogTarget.All);
                    continue;
                }

                item.MatchAccepted = true;
                item.RequiresManualReview = false;
                item.UseAbsoluteAnimeMapping = false;

                if (item.ParserPattern == ParserPattern.AnimeAbsolute && !HasMappedEpisode(item))
                {
                    await ResolveEpisodeMappingAsync(item);
                    if (!HasMappedEpisode(item))
                    {
                        skippedCount++;
                        _databaseService.UpdateSourceItem(item);
                        continue;
                    }
                }

                item.State = ItemState.Parsed;
                item.Notes = item.ParserPattern == ParserPattern.AnimeAbsolute
                    ? $"Accepted suggested identity. Using TMDb episode mapping S{item.MappedSeasonNumber:00}E{item.MappedEpisodeNumber:00}."
                    : null;
                _databaseService.UpdateSourceItem(item);
                var mappingKey = $"{item.ShowTitle}|{item.ParserPattern}|{item.Provider ?? "tmdb"}|{item.ProviderId}";
                if (savedMappings.Add(mappingKey))
                {
                    _databaseService.UpsertSeriesMapping(new SeriesMapping
                    {
                        ParsedTitle = item.ShowTitle,
                        ParserPattern = item.ParserPattern,
                        MatchedTitle = item.MatchedTitle,
                        MatchedYear = item.MatchedYear,
                        Provider = item.Provider ?? "tmdb",
                        ProviderId = item.ProviderId,
                        UseAbsoluteAnimeMapping = false
                    });
                }

                acceptedCount++;
            }

            ReloadPersistedItems();
            StatusMessage = $"Accepted {acceptedCount} suggested match(es). Skipped: {skippedCount}.";
            _progressService.Finish(StatusMessage);
        }
        finally
        {
            IsBusy = false;
            _operationQueue.Release();
        }
    }

    [RelayCommand]
    private async Task IgnoreSelected()
    {
        var queuedItems = GetQueuedSelection();
        if (queuedItems.Count == 0)
        {
            StatusMessage = "Select one or more items first.";
            return;
        }

        await _operationQueue.WaitAsync();
        IsBusy = true;
        try
        {
            var ignoredCount = 0;
            var failedCount = 0;
            var processedCount = 0;
            _progressService.Start($"Ignoring {queuedItems.Count} selected item(s)...", queuedItems.Count);

            foreach (var item in queuedItems)
            {
                await Task.Yield();
                processedCount++;
                _progressService.Report(processedCount, $"Ignoring: {item.DisplayTitle}");

                if (!string.IsNullOrWhiteSpace(item.LinkedPath) &&
                    !_hardlinkService.RemoveHardLink(item, out _, out var errorMessage))
                {
                    failedCount++;
                    item.State = ItemState.Error;
                    item.Notes = $"Could not ignore because linked file cleanup failed: {errorMessage}";
                    _databaseService.UpdateSourceItem(item);
                    continue;
                }

                item.LinkedPath = null;
                item.State = ItemState.Ignored;
                item.RequiresManualReview = false;
                item.MatchAccepted = false;
                item.Notes = "Ignored manually. Future scans preserve this state for the same file path.";
                _databaseService.UpdateSourceItem(item);
                ignoredCount++;
            }

            ReloadPersistedItems();
            StatusMessage = $"Ignored {ignoredCount} item(s). Failed: {failedCount}.";
            _progressService.Finish(StatusMessage);
        }
        finally
        {
            IsBusy = false;
            _operationQueue.Release();
        }
    }

    [RelayCommand]
    private async Task CleanupDeleted()
    {
        await _operationQueue.WaitAsync();
        IsBusy = true;
        try
        {
            var deletedItems = _allItems
                .Where(item => item.State == ItemState.Deleted)
                .ToList();
            var filesystemSuccessCount = 0;
            var filesystemFailureCount = 0;
            var noLinkedPathCount = 0;
            var processedCount = 0;
            _progressService.Start($"Cleaning up {deletedItems.Count} deleted item(s)...", Math.Max(deletedItems.Count, 1));

            foreach (var item in deletedItems)
            {
                await Task.Yield();
                processedCount++;
                _progressService.Report(processedCount, $"Cleaning up: {item.DisplayTitle}");

                if (string.IsNullOrWhiteSpace(item.LinkedPath))
                {
                    noLinkedPathCount++;
                    _logger.Info($"Deleted item has no linked path; database record will be removed directly: {item.FilePath}", LogTarget.All);
                    continue;
                }

                if (_hardlinkService.RemoveHardLink(item, out _, out var errorMessage))
                {
                    filesystemSuccessCount++;
                    item.LinkedPath = null;
                    _databaseService.UpdateSourceItem(item);
                    continue;
                }

                filesystemFailureCount++;
                item.State = ItemState.Error;
                item.Notes = $"Cleanup failed: {errorMessage}";
                _databaseService.UpdateSourceItem(item);
            }

            var deletedCount = _databaseService.DeleteSourceItemsByState(ItemState.Deleted);
            ReloadPersistedItems();
            StatusMessage = $"Cleaned up {deletedCount} deleted DB item(s). Removed library links/folders: {filesystemSuccessCount}. Direct DB cleanup: {noLinkedPathCount}. Failed filesystem cleanup: {filesystemFailureCount}.";
            _progressService.Finish(StatusMessage);
        }
        finally
        {
            IsBusy = false;
            _operationQueue.Release();
        }
    }

    [RelayCommand]
    private void ResetFilters()
    {
        SelectedStateFilter = StateFilterOptions[0];
        SelectedKindFilter = KindFilterOptions[0];
        SearchText = string.Empty;
        ApplyFilters();
    }

    public void ReloadPersistedItems()
    {
        _allItems.Clear();
        _allItems.AddRange(_databaseService.GetSourceItems());
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        Items.Clear();
        SelectedItems.Clear();
        SelectedItem = null;

        var filtered = _allItems.Where(item => _filters.All(filter => !filter.IsActive || filter.Matches(item)));
        var displayIndex = 1;
        foreach (var item in filtered)
        {
            item.DisplayIndex = displayIndex++;
            Items.Add(item);
        }

        StatusMessage = $"Showing {Items.Count} of {_allItems.Count} item(s).";
    }

    private static bool CanLink(SourceItem item)
    {
        return item.MediaKind switch
        {
            MediaKind.TvEpisode => !string.IsNullOrWhiteSpace(item.ShowTitle) &&
                                   HasOutputEpisodeNumber(item) &&
                                   !string.IsNullOrWhiteSpace(item.MatchedTitle) &&
                                   !string.IsNullOrWhiteSpace(item.ProviderId) &&
                                   !item.RequiresManualReview &&
                                   item.MatchAccepted,
            MediaKind.Movie => !string.IsNullOrWhiteSpace(item.MovieTitle),
            _ => false
        };
    }

    private async Task ResolveTvIdentityAsync(IReadOnlyList<SourceItem> scannedItems)
    {
        var tvItems = scannedItems.Where(item => item.MediaKind == MediaKind.TvEpisode).ToList();
        var processedCount = 0;
        _progressService.Start($"Resolving metadata for {tvItems.Count} TV item(s)...", Math.Max(tvItems.Count, 1));
        foreach (var item in tvItems)
        {
            processedCount++;
            _progressService.Report(processedCount, $"Resolving metadata: {item.DisplayTitle}");
            if (string.IsNullOrWhiteSpace(item.ShowTitle))
            {
                item.State = ItemState.NeedsReview;
                item.RequiresManualReview = true;
                item.Notes = "TV item has no parsed show title.";
                continue;
            }

            var savedMapping = _databaseService.GetSeriesMapping(item.ShowTitle, item.ParserPattern);
            if (savedMapping is not null)
            {
                var validation = await _metadataProvider.ValidateTvSeriesMatchAsync(TvSeriesMatchRequest.FromSourceItem(item), savedMapping.ProviderId);
                if (validation.IsAvailable && validation.IsValid)
                {
                    ApplyMapping(item, savedMapping);
                    await ResolveEpisodeMappingAsync(item);
                    continue;
                }

                item.State = ItemState.NeedsReview;
                item.RequiresManualReview = true;
                item.MatchAccepted = false;
                item.MatchReason = validation.Reason ?? validation.ErrorMessage ?? "Saved mapping failed validation.";
                item.Notes = $"Saved mapping ignored: {savedMapping.MatchedTitle} ({savedMapping.MatchedYear}) [tmdbid-{savedMapping.ProviderId}]. {item.MatchReason}";
                _logger.Warning($"Ignored saved series mapping for {item.FilePath}: {item.Notes}", LogTarget.All);

                if (!validation.IsAvailable)
                {
                    continue;
                }
            }

            var matchResult = await _metadataProvider.MatchTvSeriesAsync(item);
            if (!matchResult.IsAvailable || matchResult.BestCandidate is null)
            {
                item.State = ItemState.NeedsReview;
                item.RequiresManualReview = true;
                item.MatchAccepted = false;
                item.MatchReason = matchResult.ErrorMessage ?? "No TMDb match found.";
                item.Notes = item.MatchReason;
                continue;
            }

            ApplyCandidate(item, matchResult.BestCandidate);
            await ResolveEpisodeMappingAsync(item);
        }
    }

    private static void ApplyMapping(SourceItem item, SeriesMapping mapping)
    {
        item.MatchedTitle = mapping.MatchedTitle;
        item.MatchedYear = mapping.MatchedYear;
        item.Provider = mapping.Provider;
        item.ProviderId = mapping.ProviderId;
        item.MatchConfidence = 100;
        item.MatchReason = "Accepted saved series mapping.";
        item.RequiresManualReview = false;
        item.MatchAccepted = true;
        item.UseAbsoluteAnimeMapping = mapping.UseAbsoluteAnimeMapping;
        item.State = ItemState.Parsed;
        item.Notes = item.UseAbsoluteAnimeMapping
            ? "Using saved anime absolute numbering mapping."
            : null;
    }

    private static void ApplyCandidate(SourceItem item, TmdbTvCandidate candidate)
    {
        item.MatchedTitle = candidate.Name;
        item.MatchedYear = candidate.FirstAirYear;
        item.Provider = "tmdb";
        item.ProviderId = candidate.Id.ToString();
        item.MatchConfidence = candidate.Confidence;
        item.MatchReason = candidate.MatchReason;

        var highConfidence = candidate.Confidence >= 75;
        var animeAbsoluteNeedsMappingChoice = item.ParserPattern == ParserPattern.AnimeAbsolute;
        item.RequiresManualReview = !highConfidence || animeAbsoluteNeedsMappingChoice;
        item.MatchAccepted = highConfidence && !animeAbsoluteNeedsMappingChoice;
        item.UseAbsoluteAnimeMapping = false;
        item.State = item.RequiresManualReview ? ItemState.NeedsReview : ItemState.Parsed;
        item.Notes = item.RequiresManualReview
            ? animeAbsoluteNeedsMappingChoice
                ? $"Suggested {candidate.Name} ({candidate.FirstAirYear}) [tmdbid-{candidate.Id}], but absolute anime season mapping must be accepted manually."
                : $"Low-confidence match suggestion: {candidate.Name} ({candidate.FirstAirYear}) [tmdbid-{candidate.Id}]. {candidate.MatchReason}"
            : $"Auto-matched {candidate.Name} ({candidate.FirstAirYear}) [tmdbid-{candidate.Id}].";
    }

    private async Task ResolveEpisodeMappingAsync(SourceItem item)
    {
        if (item.ParserPattern != ParserPattern.AnimeAbsolute ||
            item.MediaKind != MediaKind.TvEpisode ||
            string.IsNullOrWhiteSpace(item.ProviderId))
        {
            return;
        }

        var mappingResult = await _metadataProvider.MapTvEpisodeAsync(item);
        if (mappingResult.IsMapped &&
            mappingResult.SeasonNumber is not null &&
            mappingResult.EpisodeNumber is not null)
        {
            item.MappedSeasonNumber = mappingResult.SeasonNumber;
            item.MappedEpisodeNumber = mappingResult.EpisodeNumber;
            item.EpisodeMappingSource = mappingResult.Source;
            item.EpisodeMappingConfidence = mappingResult.Confidence;
            item.EpisodeMappingReason = mappingResult.Reason;
            item.UseAbsoluteAnimeMapping = false;

            var highSeriesConfidence = item.MatchConfidence >= 75;
            var highMappingConfidence = mappingResult.Confidence >= 75;
            item.RequiresManualReview = !highSeriesConfidence || !highMappingConfidence;
            item.MatchAccepted = highSeriesConfidence && highMappingConfidence;
            item.State = item.MatchAccepted ? ItemState.Parsed : ItemState.NeedsReview;
            item.Notes = item.MatchAccepted
                ? $"Mapped absolute anime episode {item.EpisodeNumber} to TMDb S{item.MappedSeasonNumber:00}E{item.MappedEpisodeNumber:00}."
                : $"Suggested episode mapping S{item.MappedSeasonNumber:00}E{item.MappedEpisodeNumber:00} needs review. {mappingResult.Reason}";
            return;
        }

        item.MappedSeasonNumber = null;
        item.MappedEpisodeNumber = null;
        item.EpisodeMappingSource = null;
        item.EpisodeMappingConfidence = null;
        item.EpisodeMappingReason = mappingResult.Reason ?? mappingResult.ErrorMessage;
        item.RequiresManualReview = true;
        item.MatchAccepted = false;
        item.UseAbsoluteAnimeMapping = false;
        item.State = ItemState.NeedsReview;
        item.Notes = $"Absolute anime episode detected, but TMDb season mapping is uncertain. {item.EpisodeMappingReason}";
    }

    private static bool HasOutputEpisodeNumber(SourceItem item)
    {
        return item.ParserPattern == ParserPattern.AnimeAbsolute
            ? HasMappedEpisode(item)
            : item.SeasonNumber is not null && item.EpisodeNumber is not null;
    }

    private static bool HasMappedEpisode(SourceItem item)
    {
        return item.MappedSeasonNumber is not null && item.MappedEpisodeNumber is not null;
    }

    private List<SourceItem> GetQueuedSelection()
    {
        if (SelectedItems.Count > 0)
        {
            return SelectedItems.ToList();
        }

        return SelectedItem is null ? [] : [SelectedItem];
    }

    private static IReadOnlyList<FilterOption<ItemState>> BuildStateFilterOptions()
    {
        var options = new List<FilterOption<ItemState>>
        {
            new() { Label = "All states", Value = null }
        };
        options.AddRange(Enum.GetValues<ItemState>().Select(state => new FilterOption<ItemState>
        {
            Label = state.ToString(),
            Value = state
        }));
        return options;
    }

    private static IReadOnlyList<FilterOption<MediaKind>> BuildKindFilterOptions()
    {
        var options = new List<FilterOption<MediaKind>>
        {
            new() { Label = "All kinds", Value = null }
        };
        options.AddRange(Enum.GetValues<MediaKind>().Select(kind => new FilterOption<MediaKind>
        {
            Label = kind.ToString(),
            Value = kind
        }));
        return options;
    }
}
