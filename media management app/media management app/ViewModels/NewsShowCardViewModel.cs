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
        int pendingEpisodes)
    {
        ShowId = showId;
        Title = title;
        PosterPath = posterPath;
        SeriesStatusLabel = seriesStatusLabel;
        PendingEpisodes = pendingEpisodes;
    }

    public long ShowId { get; }

    public string Title { get; }

    public string? PosterPath { get; }

    public string SeriesStatusLabel { get; }

    public int PendingEpisodes { get; }

    public string PendingLabel => PendingEpisodes == 0
        ? "Up to date"
        : $"{PendingEpisodes} pending";

    [ObservableProperty]
    private ImageSource? posterImage;
}
