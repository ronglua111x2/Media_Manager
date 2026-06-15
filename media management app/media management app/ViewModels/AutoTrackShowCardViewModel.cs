using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;using media_management_app.Models;
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
        TrackedEpisode? latestPendingEpisode,
        AutoTrackSettings settings,
        IReadOnlyList<string> downloadFolderOptions,
        ITrackedShowService trackedShowService,
        Action? onSettingsSaved = null)
    {
        _trackedShowService = trackedShowService;
        _onSettingsSaved = onSettingsSaved;
        _settings = settings;

        ShowId = show.Id;
        Title = show.DisplayTitle;
        PosterPath = show.PosterPath;
        CheckpointLabel = show.AutoTrackCheckpointLabel;
        SeriesStatusLabel = show.SeriesStatusLabel;
        PendingEpisodes = pendingEpisodes;
        DownloadFolder = show.AutoTrackDownloadFolder ?? string.Empty;
        AutoReconcileAndLink = show.AutoTrackAutoReconcileAndLink;
        DownloadFolderOptions = new ObservableCollection<string>(downloadFolderOptions);

        UseCustomSchedule = show.AutoTrackAnchorDayOfWeek is not null || !string.IsNullOrWhiteSpace(show.AutoTrackAnchorTimeLocal);
        CustomAnchorDay = show.AutoTrackAnchorDayOfWeek ?? settings.AnchorDayOfWeek;
        CustomAnchorTimeLocal = show.AutoTrackAnchorTimeLocal ?? settings.AnchorTimeLocal;

        UseCustomQuality = show.AutoTrackMinQuality is not null ||
                           show.AutoTrackMinSeeders is not null ||
                           show.AutoTrackMinFileSizeMb is not null ||
                           show.AutoTrackMaxFileSizeMb is not null ||
                           !string.IsNullOrWhiteSpace(show.AutoTrackAllowedQualities);
        CustomMinQuality = show.AutoTrackMinQuality ?? settings.Quality?.MinQuality ?? "1080p";
        CustomMinSeeders = show.AutoTrackMinSeeders ?? settings.Quality?.MinSeeders ?? 0;
        CustomMinFileSizeMb = show.AutoTrackMinFileSizeMb ?? settings.Quality?.MinFileSizeMb ?? 0;
        CustomMaxFileSizeMb = show.AutoTrackMaxFileSizeMb ?? settings.Quality?.MaxFileSizeMb ?? 0;
        CustomAllowedQualities = show.AutoTrackAllowedQualities ??
                                 string.Join(", ", settings.Quality?.AllowedQualities ?? []);

        TmdbStatusLine = BuildTmdbStatusLine(show, settings);
        HuntStatusLine = BuildHuntStatusLine(show, latestPendingEpisode);
        ScheduleStatusLine = $"Schedule: {AutoTrackWeekAnchor.FormatEffectiveAnchor(show, settings)}";

        if (!string.IsNullOrWhiteSpace(DownloadFolder) &&
            !DownloadFolderOptions.Contains(DownloadFolder, StringComparer.OrdinalIgnoreCase))
        {
            DownloadFolderOptions.Insert(0, DownloadFolder);
        }

        _isInitializing = false;
    }

    private readonly AutoTrackSettings _settings;

    public long ShowId { get; }

    public string Title { get; }

    public string? PosterPath { get; }

    public string CheckpointLabel { get; }

    public string SeriesStatusLabel { get; }

    public int PendingEpisodes { get; }

    public string TmdbStatusLine { get; }

    public string HuntStatusLine { get; }

    public string ScheduleStatusLine { get; }

    public Array AnchorDayOptions => Enum.GetValues(typeof(DayOfWeek));

    public ObservableCollection<string> DownloadFolderOptions { get; }

    [ObservableProperty]
    private string downloadFolder = string.Empty;

    [ObservableProperty]
    private bool autoReconcileAndLink = true;

    [ObservableProperty]
    private bool useCustomSchedule;

    [ObservableProperty]
    private DayOfWeek customAnchorDay = DayOfWeek.Sunday;

    [ObservableProperty]
    private string customAnchorTimeLocal = "21:00";

    [ObservableProperty]
    private bool useCustomQuality;

    [ObservableProperty]
    private string customMinQuality = "1080p";

    [ObservableProperty]
    private int customMinSeeders;

    [ObservableProperty]
    private int customMinFileSizeMb;

    [ObservableProperty]
    private int customMaxFileSizeMb;

    [ObservableProperty]
    private string customAllowedQualities = string.Empty;

    public bool HasDownloadFolder => !string.IsNullOrWhiteSpace(DownloadFolder);

    public bool NeedsDownloadFolder => !HasDownloadFolder;

    public string PosterUrl => string.IsNullOrWhiteSpace(PosterPath)
        ? string.Empty
        : $"https://image.tmdb.org/t/p/w154{PosterPath}";

    [ObservableProperty]
    private ImageSource? posterImage;

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

    partial void OnUseCustomScheduleChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        if (!value)
        {
            _trackedShowService.UpdateAutoTrackScheduleOverrides(ShowId, null, null, clearOverrides: true);
        }
        else
        {
            SaveScheduleOverrides();
        }

        _onSettingsSaved?.Invoke();
    }

    partial void OnCustomAnchorDayChanged(DayOfWeek value)
    {
        if (_isInitializing || !UseCustomSchedule)
        {
            return;
        }

        SaveScheduleOverrides();
        _onSettingsSaved?.Invoke();
    }

    partial void OnCustomAnchorTimeLocalChanged(string value)
    {
        if (_isInitializing || !UseCustomSchedule)
        {
            return;
        }

        SaveScheduleOverrides();
        _onSettingsSaved?.Invoke();
    }

    partial void OnUseCustomQualityChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        if (!value)
        {
            _trackedShowService.UpdateAutoTrackQualityOverrides(ShowId, null, null, null, null, null, clearOverrides: true);
        }
        else
        {
            SaveQualityOverrides();
        }

        _onSettingsSaved?.Invoke();
    }

    partial void OnCustomMinQualityChanged(string value) => SaveQualityIfEnabled();

    partial void OnCustomMinSeedersChanged(int value) => SaveQualityIfEnabled();

    partial void OnCustomMinFileSizeMbChanged(int value) => SaveQualityIfEnabled();

    partial void OnCustomMaxFileSizeMbChanged(int value) => SaveQualityIfEnabled();

    partial void OnCustomAllowedQualitiesChanged(string value) => SaveQualityIfEnabled();

    private void SaveQualityIfEnabled()
    {
        if (_isInitializing || !UseCustomQuality)
        {
            return;
        }

        SaveQualityOverrides();
        _onSettingsSaved?.Invoke();
    }

    private void SaveScheduleOverrides()
    {
        _trackedShowService.UpdateAutoTrackScheduleOverrides(
            ShowId,
            CustomAnchorDay,
            string.IsNullOrWhiteSpace(CustomAnchorTimeLocal) ? "21:00" : CustomAnchorTimeLocal.Trim(),
            clearOverrides: false);
    }

    private void SaveQualityOverrides()
    {
        _trackedShowService.UpdateAutoTrackQualityOverrides(
            ShowId,
            string.IsNullOrWhiteSpace(CustomMinQuality) ? null : CustomMinQuality.Trim(),
            CustomMinSeeders,
            CustomMinFileSizeMb > 0 ? CustomMinFileSizeMb : null,
            CustomMaxFileSizeMb > 0 ? CustomMaxFileSizeMb : null,
            string.IsNullOrWhiteSpace(CustomAllowedQualities) ? null : CustomAllowedQualities.Trim(),
            clearOverrides: false);
    }

    private static string BuildTmdbStatusLine(TrackedShow show, AutoTrackSettings settings)
    {
        var now = DateTime.Now;
        if (!string.IsNullOrWhiteSpace(show.AutoTrackLastTmdbWeekKey))
        {
            return $"Last TMDB: {show.AutoTrackLastTmdbWeekKey}";
        }

        if (AutoTrackWeekAnchor.IsPastAnchorThisWeek(show, now, settings))
        {
            return "Next TMDB: pending discovery";
        }

        return $"Next TMDB: after {AutoTrackWeekAnchor.FormatEffectiveAnchor(show, settings)}";
    }

    private static string BuildHuntStatusLine(TrackedShow show, TrackedEpisode? latestPendingEpisode)
    {
        if (latestPendingEpisode is not null)
        {
            return $"Hunting: S{latestPendingEpisode.SeasonNumber:00}E{latestPendingEpisode.EpisodeNumber:00}";
        }

        return show.AutoTrackTmdbState switch
        {
            AutoTrackTmdbState.DormantCaughtUp => "Caught up (dormant until next anchor)",
            AutoTrackTmdbState.FinishedComplete => "Finished and complete",
            _ => "No pending hunt"
        };
    }
}
