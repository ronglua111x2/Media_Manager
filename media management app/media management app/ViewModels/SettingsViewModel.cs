using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Common;
using media_management_app.Services;
using WinForms = System.Windows.Forms;

namespace media_management_app.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IDatabaseService _databaseService;
    private readonly ILibraryPathResolver _libraryPathResolver;
    private readonly IAppLogger _logger;
    private bool _isLoadingSettings;

    [ObservableProperty]
    private string stateFolder = string.Empty;

    [ObservableProperty]
    private string defaultLibraryFolderName = string.Empty;

    [ObservableProperty]
    private string? tmdbReadAccessToken;

    [ObservableProperty]
    private string? selectedSourceFolder;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    public SettingsViewModel(
        ISettingsService settingsService,
        IDatabaseService databaseService,
        ILibraryPathResolver libraryPathResolver,
        IAppLogger logger)
    {
        _settingsService = settingsService;
        _databaseService = databaseService;
        _libraryPathResolver = libraryPathResolver;
        _logger = logger;
        SourceFolders = [];
        LibraryRootPreview = [];
        LoadFromSettings();
    }

    public ObservableCollection<string> SourceFolders { get; }

    public ObservableCollection<string> LibraryRootPreview { get; }

    [RelayCommand]
    private void BrowseStateFolder()
    {
        var selected = BrowseFolder(StateFolder, "Select state folder");
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        StateFolder = selected;
    }

    [RelayCommand]
    private void AddSourceFolder()
    {
        var selected = BrowseFolder(null, "Add source folder");
        if (string.IsNullOrWhiteSpace(selected) ||
            SourceFolders.Any(folder => string.Equals(folder, selected, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        SourceFolders.Add(selected);
        SelectedSourceFolder = selected;
        RefreshLibraryRootPreview();
    }

    [RelayCommand]
    private void RemoveSelectedSourceFolder()
    {
        if (string.IsNullOrWhiteSpace(SelectedSourceFolder))
        {
            return;
        }

        SourceFolders.Remove(SelectedSourceFolder);
        SelectedSourceFolder = SourceFolders.FirstOrDefault();
        RefreshLibraryRootPreview();
    }

    [RelayCommand]
    private void Save()
    {
        _settingsService.Current.StateFolder = StateFolder;
        _settingsService.Current.SourceFolders = SourceFolders.ToList();
        _settingsService.Current.LibraryRootMode = LibraryRootMode.AutoPerDrive;
        _settingsService.Current.DefaultLibraryFolderName = string.IsNullOrWhiteSpace(DefaultLibraryFolderName)
            ? AppConstants.DefaultLibraryFolderName
            : DefaultLibraryFolderName.Trim();
        _settingsService.Current.TmdbReadAccessToken = string.IsNullOrWhiteSpace(TmdbReadAccessToken) ? null : TmdbReadAccessToken;
        _settingsService.Save();
        _databaseService.Initialize(_settingsService.Current.StateFolder);
        RefreshLibraryRootPreview();
        StatusMessage = $"Saved settings to {_settingsService.SettingsFilePath}";
        _logger.Info($"Saved settings to {_settingsService.SettingsFilePath}", LogTarget.All);
    }

    partial void OnDefaultLibraryFolderNameChanged(string value)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        RefreshLibraryRootPreview();
    }

    private void LoadFromSettings()
    {
        _logger.Info(
            $"Loading Settings UI from service. SettingsFile='{_settingsService.SettingsFilePath}', SourceFolders={_settingsService.Current.SourceFolders.Count}, StateFolder='{_settingsService.Current.StateFolder}'",
            LogTarget.All);

        _isLoadingSettings = true;
        try
        {
            StateFolder = _settingsService.Current.StateFolder;
            DefaultLibraryFolderName = _settingsService.Current.DefaultLibraryFolderName;
            TmdbReadAccessToken = _settingsService.Current.TmdbReadAccessToken;

            SourceFolders.Clear();
            foreach (var folder in _settingsService.Current.SourceFolders)
            {
                _logger.Debug($"Adding source folder to Settings UI: {folder}", LogTarget.File | LogTarget.Console);
                SourceFolders.Add(folder);
            }

            SelectedSourceFolder = SourceFolders.FirstOrDefault();
        }
        finally
        {
            _isLoadingSettings = false;
        }

        RefreshLibraryRootPreview();
        _logger.Info($"Settings UI loaded. VisibleSourceFolders={SourceFolders.Count}, SelectedSourceFolder='{SelectedSourceFolder ?? "<none>"}'", LogTarget.All);
    }

    private void RefreshLibraryRootPreview()
    {
        _settingsService.Current.DefaultLibraryFolderName = string.IsNullOrWhiteSpace(DefaultLibraryFolderName)
            ? AppConstants.DefaultLibraryFolderName
            : DefaultLibraryFolderName.Trim();

        LibraryRootPreview.Clear();
        _logger.Debug($"Refreshing library root preview for {SourceFolders.Count} source folder(s)", LogTarget.File | LogTarget.Console);
        foreach (var root in _libraryPathResolver.GetPreviewRoots(SourceFolders))
        {
            LibraryRootPreview.Add($"{Path.GetPathRoot(root)} -> {root}");
            LibraryRootPreview.Add($"    Shows: {Path.Combine(root, AppConstants.ShowsFolderName)}");
            LibraryRootPreview.Add($"    Movies: {Path.Combine(root, AppConstants.MoviesFolderName)}");
            _logger.Debug($"Library root preview generated: {root}", LogTarget.File | LogTarget.Console);
        }
    }

    private static string? BrowseFolder(string? initialFolder, string description)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = Directory.Exists(initialFolder) ? initialFolder : string.Empty
        };

        return dialog.ShowDialog() == WinForms.DialogResult.OK ? dialog.SelectedPath : null;
    }
}
