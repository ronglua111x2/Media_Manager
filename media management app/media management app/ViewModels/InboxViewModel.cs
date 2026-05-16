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
    private readonly ISettingsService _settingsService;
    private readonly IDatabaseService _databaseService;
    private readonly IScannerService _scannerService;
    private readonly IHardlinkService _hardlinkService;
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

    public InboxViewModel(
        ISettingsService settingsService,
        IDatabaseService databaseService,
        IScannerService scannerService,
        IHardlinkService hardlinkService,
        IAppLogger logger)
    {
        _settingsService = settingsService;
        _databaseService = databaseService;
        _scannerService = scannerService;
        _hardlinkService = hardlinkService;
        _logger = logger;
        Items = new ObservableCollection<SourceItem>();
        StateFilterOptions = BuildStateFilterOptions();
        KindFilterOptions = BuildKindFilterOptions();
        SelectedStateFilter = StateFilterOptions[0];
        SelectedKindFilter = KindFilterOptions[0];
        _filters =
        [
            new SourceItemStateFilter(() => SelectedStateFilter?.Value),
            new SourceItemKindFilter(() => SelectedKindFilter?.Value)
        ];
        ReloadPersistedItems();
    }

    public ObservableCollection<SourceItem> Items { get; }

    public ObservableCollection<SourceItem> SelectedItems { get; } = [];

    public IReadOnlyList<FilterOption<ItemState>> StateFilterOptions { get; }

    public IReadOnlyList<FilterOption<MediaKind>> KindFilterOptions { get; }

    [RelayCommand]
    private void Scan()
    {
        IsBusy = true;
        try
        {
            var scanned = _scannerService.Scan(_settingsService.Current.SourceFolders);
            _databaseService.UpsertSourceItems(scanned);
            var deletedCount = _databaseService.MarkMissingSourceItems(_settingsService.Current.SourceFolders, scanned.Select(item => item.FilePath));
            ReloadPersistedItems();
            StatusMessage = $"Scanned {scanned.Count} video item(s). Marked deleted: {deletedCount}.";
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
            StatusMessage = $"Creating hardlinks for {queuedItems.Count} selected item(s)...";

            foreach (var item in queuedItems)
            {
                await Task.Yield();
                if (item.State is ItemState.NeedsReview or ItemState.Deleted || !CanLink(item))
                {
                    failureCount++;
                    item.Notes = item.State == ItemState.Deleted
                        ? "Source file is deleted and cannot be linked."
                        : "Item needs review before linking.";
                    _databaseService.UpdateSourceItem(item);
                    _logger.Warning($"Skipped item that needs review before linking: {item.FilePath}", LogTarget.Ui | LogTarget.Console);
                    continue;
                }

                if (_hardlinkService.CreateHardLink(item, _settingsService.Current.OutputLibraryFolder, out var createdPath, out var errorMessage))
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
            StatusMessage = $"Removing hardlinks for {queuedItems.Count} selected item(s)...";

            foreach (var item in queuedItems)
            {
                await Task.Yield();
                if (_hardlinkService.RemoveHardLink(item, _settingsService.Current.OutputLibraryFolder, out _, out var errorMessage))
                {
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
            StatusMessage = $"Remove queue finished. Removed/cleared: {successCount}. Failed/skipped: {failureCount}.";
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

            foreach (var item in deletedItems.Where(item => !string.IsNullOrWhiteSpace(item.LinkedPath)))
            {
                await Task.Yield();
                if (_hardlinkService.RemoveHardLink(item, _settingsService.Current.OutputLibraryFolder, out _, out var errorMessage))
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
            StatusMessage = $"Cleaned up {deletedCount} deleted DB item(s). Removed library links/folders: {filesystemSuccessCount}. Failed filesystem cleanup: {filesystemFailureCount}.";
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
                                   item.SeasonNumber is not null &&
                                   item.EpisodeNumber is not null,
            MediaKind.Movie => !string.IsNullOrWhiteSpace(item.MovieTitle),
            _ => false
        };
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
