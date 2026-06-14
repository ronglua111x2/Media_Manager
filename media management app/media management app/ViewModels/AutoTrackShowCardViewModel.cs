using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Models;
using media_management_app.Services;

namespace media_management_app.ViewModels;

public sealed partial class AutoTrackShowCardViewModel : ObservableObject
{
    private readonly ITrackedShowService _trackedShowService;
    private readonly Action? _onSettingsSaved;
    private bool _isInitializing = true;

    public AutoTrackShowCardViewModel(
        TrackedShow show,
        int pendingEpisodes,
        IReadOnlyList<string> downloadFolderOptions,
        ITrackedShowService trackedShowService,
        Action? onSettingsSaved = null)
    {
        _trackedShowService = trackedShowService;
        _onSettingsSaved = onSettingsSaved;

        ShowId = show.Id;
        Title = show.DisplayTitle;
        PosterPath = show.PosterPath;
        CheckpointLabel = show.AutoTrackCheckpointLabel;
        SeriesStatusLabel = show.SeriesStatusLabel;
        PendingEpisodes = pendingEpisodes;
        DownloadFolder = show.AutoTrackDownloadFolder ?? string.Empty;
        AutoReconcileAndLink = show.AutoTrackAutoReconcileAndLink;
        DownloadFolderOptions = new ObservableCollection<string>(downloadFolderOptions);

        if (!string.IsNullOrWhiteSpace(DownloadFolder) &&
            !DownloadFolderOptions.Contains(DownloadFolder, StringComparer.OrdinalIgnoreCase))
        {
            DownloadFolderOptions.Insert(0, DownloadFolder);
        }

        _isInitializing = false;
    }

    public long ShowId { get; }

    public string Title { get; }

    public string? PosterPath { get; }

    public string CheckpointLabel { get; }

    public string SeriesStatusLabel { get; }

    public int PendingEpisodes { get; }

    public ObservableCollection<string> DownloadFolderOptions { get; }

    [ObservableProperty]
    private string downloadFolder = string.Empty;

    [ObservableProperty]
    private bool autoReconcileAndLink = true;

    public bool HasDownloadFolder => !string.IsNullOrWhiteSpace(DownloadFolder);

    public bool NeedsDownloadFolder => !HasDownloadFolder;

    public string PosterUrl => string.IsNullOrWhiteSpace(PosterPath)
        ? string.Empty
        : $"https://image.tmdb.org/t/p/w154{PosterPath}";

    public string PendingLabel => PendingEpisodes == 0
        ? "Up to date"
        : $"{PendingEpisodes} pending";

    partial void OnAutoReconcileAndLinkChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        _trackedShowService.UpdateAutoTrackReconcileAndLink(ShowId, value);
        _onSettingsSaved?.Invoke();
    }

    partial void OnDownloadFolderChanged(string value)
    {
        OnPropertyChanged(nameof(HasDownloadFolder));
        OnPropertyChanged(nameof(NeedsDownloadFolder));
        if (_isInitializing || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        _trackedShowService.UpdateAutoTrackDownloadFolder(ShowId, value.Trim());
        _onSettingsSaved?.Invoke();
    }
}
