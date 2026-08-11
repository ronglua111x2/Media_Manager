using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace media_management_app.ViewModels;

public sealed partial class NewsShowCardViewModel : ObservableObject
{
    public NewsShowCardViewModel(
        long showId,
        string title,
        string? posterPath,
        string seriesStatusLabel,
        int pendingEpisodes,
        string? weeklyAirDayLabel,
        string anchorLabel,
        bool isNotAired)
    {
        ShowId = showId;
        Title = title;
        PosterPath = posterPath;
        SeriesStatusLabel = seriesStatusLabel;
        PendingEpisodes = pendingEpisodes;
        WeeklyAirDayLabel = weeklyAirDayLabel ?? string.Empty;
        AnchorLabel = anchorLabel;
        IsNotAired = isNotAired;
    }

    public long ShowId { get; }

    public string Title { get; }

    public string? PosterPath { get; }

    public string SeriesStatusLabel { get; }

    public int PendingEpisodes { get; }

    public bool IsNotAired { get; }

    public bool IsUpToDate => !IsNotAired && PendingEpisodes == 0;

    public bool HasPendingEpisodes => !IsNotAired && PendingEpisodes > 0;

    public string PendingLabel => IsNotAired
        ? "Not Aired"
        : IsUpToDate
            ? "Up to date"
            : $"{PendingEpisodes} pending";

    public string WeeklyAirDayLabel { get; }

    public bool HasWeeklyAirDay => !string.IsNullOrWhiteSpace(WeeklyAirDayLabel);

    public string AnchorLabel { get; }

    public bool HasAnchorLabel => !string.IsNullOrWhiteSpace(AnchorLabel);

    public bool HasScheduleMeta => HasWeeklyAirDay || HasAnchorLabel;

    [ObservableProperty]
    private ImageSource? posterImage;
}
