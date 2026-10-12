using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using media_management_app.Common;
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
        IReadOnlyList<TrackedEpisode> huntBatchEpisodes,
        AutoTrackSettings settings,
        IReadOnlyList<string> downloadFolderOptions,
        ITrackedShowService trackedShowService,
        IRecipeService recipeService,
        AutoTorrentSettings autoTorrentSettings,
        string? lastHuntFailureDetail = null,
        Action? onSettingsSaved = null,
        bool overviewExpanded = false,
        Action<bool>? onOverviewExpandedChanged = null)
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
        CustomAnchorTime = AutoTrackWeekAnchor.ToTimePickerValue(CustomAnchorTimeLocal);

        var recipe = recipeService.GetRecipeOrDefault(show.RecipeId, MediaKind.TvEpisode);
        EpisodeRecipeSummary = new CartRecipeSummaryViewModel(
            recipe,
            autoTorrentSettings,
            CartRecipeOverrideSet.Parse(show.AutoTrackEpisodeOverridesJson),
            value =>
            {
                _trackedShowService.UpdateAutoTrackRecipeOverrides(ShowId, value);
                _onSettingsSaved?.Invoke();
            },
            overviewScrollGroupName: $"AutoTrackRecipe-{show.Id}",
            overrideScopeLabel: "Auto-Track hunts",
            canToggleOverview: true,
            isOverviewExpanded: overviewExpanded,
            onOverviewExpandedChanged: onOverviewExpandedChanged);

        TmdbStatusLine = BuildTmdbStatusLine(show, settings);
        HuntStatusLine = BuildHuntStatusLine(show, huntBatchEpisodes, lastHuntFailureDetail);
        ScheduleStatusLine = BuildScheduleStatusLine();

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

    [ObservableProperty]
    private string scheduleStatusLine = string.Empty;

    public IReadOnlyList<DayOfWeek> AnchorDayOptions => Enum.GetValues<DayOfWeek>();

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
    private DateTime? customAnchorTime;

    public CartRecipeSummaryViewModel EpisodeRecipeSummary { get; }

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

        ScheduleStatusLine = BuildScheduleStatusLine();
        _onSettingsSaved?.Invoke();
    }

    partial void OnCustomAnchorDayChanged(DayOfWeek value)
    {
        if (_isInitializing || !UseCustomSchedule)
        {
            return;
        }

        SaveScheduleOverrides();
        ScheduleStatusLine = BuildScheduleStatusLine();
        _onSettingsSaved?.Invoke();
    }

    partial void OnCustomAnchorTimeLocalChanged(string value)
    {
        if (_isInitializing || !UseCustomSchedule)
        {
            return;
        }

        SaveScheduleOverrides();
        ScheduleStatusLine = BuildScheduleStatusLine();
        _onSettingsSaved?.Invoke();
    }

    partial void OnCustomAnchorTimeChanged(DateTime? value)
    {
        if (_isInitializing)
        {
            return;
        }

        var formatted = AutoTrackWeekAnchor.FormatTimeLocal(value);
        if (!string.Equals(CustomAnchorTimeLocal, formatted, StringComparison.Ordinal))
        {
            CustomAnchorTimeLocal = formatted;
            return;
        }

        if (!UseCustomSchedule)
        {
            return;
        }

        SaveScheduleOverrides();
        ScheduleStatusLine = BuildScheduleStatusLine();
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

    private string BuildScheduleStatusLine()
    {
        if (UseCustomSchedule)
        {
            var time = string.IsNullOrWhiteSpace(CustomAnchorTimeLocal) ? "21:00" : CustomAnchorTimeLocal.Trim();
            return $"Schedule: {CustomAnchorDay} {time}";
        }

        if (!_settings.EnforceGlobalWeeklySchedule)
        {
            return "Schedule: off (interval)";
        }

        return $"Schedule: {_settings.AnchorDayOfWeek} {AutoTrackWeekAnchor.ParseLocalTime(_settings.AnchorTimeLocal):hh\\:mm}";
    }

    private static string BuildTmdbStatusLine(TrackedShow show, AutoTrackSettings settings)
    {
        var now = DateTime.Now;
        if (show.AutoTrackLastTmdbRefreshLocal is not null)
        {
            return $"Last TMDB: {show.AutoTrackLastTmdbRefreshLocal.Value:yyyy-MM-dd HH:mm}";
        }

        if (!string.IsNullOrWhiteSpace(show.AutoTrackLastTmdbWeekKey))
        {
            return $"Last TMDB week: {show.AutoTrackLastTmdbWeekKey}";
        }

        if (AutoTrackWeekAnchor.IsPastAnchorThisWeek(show, now, settings))
        {
            return "Next TMDB: pending discovery";
        }

        return $"Next TMDB: after {AutoTrackWeekAnchor.FormatEffectiveAnchor(show, settings)}";
    }

    private static string BuildHuntStatusLine(
        TrackedShow show,
        IReadOnlyList<TrackedEpisode> huntBatchEpisodes,
        string? lastHuntFailureDetail)
    {
        var line = AutoTrackTmdbEligibility.FormatHuntStatusLine(huntBatchEpisodes);
        if (!string.IsNullOrEmpty(line))
        {
            return line;
        }

        if (!string.IsNullOrWhiteSpace(lastHuntFailureDetail))
        {
            return $"Last hunt failed: {lastHuntFailureDetail}";
        }

        return show.AutoTrackTmdbState switch
        {
            AutoTrackTmdbState.DormantCaughtUp => "Caught up (dormant until next anchor)",
            AutoTrackTmdbState.FinishedComplete => "Finished and complete",
            _ => "No pending hunt"
        };
    }
}
