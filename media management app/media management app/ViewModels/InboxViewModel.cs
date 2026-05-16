using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace media_management_app.ViewModels;

public partial class InboxViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IDatabaseService _databaseService;
    private readonly IScannerService _scannerService;
    private readonly IHardlinkService _hardlinkService;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _operationQueue = new(1, 1);

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private SourceItem? selectedItem;

    [ObservableProperty]
    private string statusMessage = string.Empty;

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
        ReloadPersistedItems();
    }

    public ObservableCollection<SourceItem> Items { get; }

    public ObservableCollection<SourceItem> SelectedItems { get; } = [];

    [RelayCommand]
    private void Scan()
    {
        IsBusy = true;
        try
        {
            var scanned = _scannerService.Scan(_settingsService.Current.SourceFolders);
            _databaseService.UpsertSourceItems(scanned);
            ReloadPersistedItems();
            StatusMessage = $"Scanned {scanned.Count} video item(s).";
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
                if (item.State == ItemState.NeedsReview || !CanLink(item))
                {
                    failureCount++;
                    item.State = ItemState.NeedsReview;
                    item.Notes = "Item needs review before linking.";
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
                if (_hardlinkService.RemoveHardLink(item, out _, out var errorMessage))
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

    public void ReloadPersistedItems()
    {
        Items.Clear();
        foreach (var item in _databaseService.GetSourceItems())
        {
            Items.Add(item);
        }
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
}
