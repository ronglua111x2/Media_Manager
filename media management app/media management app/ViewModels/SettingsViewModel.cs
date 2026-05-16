using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Common;
using media_management_app.Services;

namespace media_management_app.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IDatabaseService _databaseService;
    private readonly IAppLogger _logger;

    [ObservableProperty]
    private string stateFolder = string.Empty;

    [ObservableProperty]
    private string sourceFoldersText = string.Empty;

    [ObservableProperty]
    private string outputLibraryFolder = string.Empty;

    [ObservableProperty]
    private string? tmdbReadAccessToken;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    public SettingsViewModel(ISettingsService settingsService, IDatabaseService databaseService, IAppLogger logger)
    {
        _settingsService = settingsService;
        _databaseService = databaseService;
        _logger = logger;
        LoadFromSettings();
    }

    [RelayCommand]
    private void Save()
    {
        _settingsService.Current.StateFolder = StateFolder;
        _settingsService.Current.SourceFolders = SourceFoldersText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        _settingsService.Current.OutputLibraryFolder = OutputLibraryFolder;
        _settingsService.Current.TmdbReadAccessToken = string.IsNullOrWhiteSpace(TmdbReadAccessToken) ? null : TmdbReadAccessToken;
        _settingsService.Save();
        _databaseService.Initialize(_settingsService.Current.StateFolder);
        StatusMessage = $"Saved settings to {_settingsService.SettingsFilePath}";
        _logger.Info($"Saved settings to {_settingsService.SettingsFilePath}", LogTarget.All);
    }

    private void LoadFromSettings()
    {
        StateFolder = _settingsService.Current.StateFolder;
        SourceFoldersText = string.Join(Environment.NewLine, _settingsService.Current.SourceFolders);
        OutputLibraryFolder = _settingsService.Current.OutputLibraryFolder;
        TmdbReadAccessToken = _settingsService.Current.TmdbReadAccessToken;
    }
}
