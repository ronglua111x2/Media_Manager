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

    [RelayCommand]
    private void LinkSelected()
    {
        if (SelectedItem is null)
        {
            StatusMessage = "Select an item first.";
            _logger.Warning("Create hardlink requested without a selected item", LogTarget.Ui | LogTarget.Console);
            return;
        }

        if (SelectedItem.State == ItemState.NeedsReview ||
            string.IsNullOrWhiteSpace(SelectedItem.ShowTitle) ||
            SelectedItem.SeasonNumber is null ||
            SelectedItem.EpisodeNumber is null)
        {
            StatusMessage = "Selected item needs review before linking.";
            _logger.Warning($"Selected item needs review before linking: {SelectedItem.FilePath}", LogTarget.Ui | LogTarget.Console);
            return;
        }

        if (_hardlinkService.CreateHardLink(SelectedItem, _settingsService.Current.OutputLibraryFolder, out var createdPath, out var errorMessage))
        {
            SelectedItem.State = ItemState.Linked;
            SelectedItem.LinkedPath = createdPath;
            SelectedItem.Notes = null;
            _databaseService.UpsertSourceItem(SelectedItem);
            ReloadPersistedItems();
            StatusMessage = $"Created hardlink: {createdPath}";
            return;
        }

        SelectedItem.State = ItemState.Error;
        SelectedItem.Notes = errorMessage;
        _databaseService.UpsertSourceItem(SelectedItem);
        ReloadPersistedItems();
        StatusMessage = $"Could not create hardlink: {errorMessage}";
    }

    public void ReloadPersistedItems()
    {
        Items.Clear();
        foreach (var item in _databaseService.GetSourceItems())
        {
            Items.Add(item);
        }
    }
}
