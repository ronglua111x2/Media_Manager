using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.ViewModels;

public sealed partial class NewsEpisodeCardViewModel : ObservableObject
{
    public NewsEpisodeCardViewModel(TrackedShow show, TrackedEpisode episode)
    {
        ShowId = show.Id;
        ShowTmdbId = show.TmdbId;
        SeasonNumber = episode.SeasonNumber;
        EpisodeNumber = episode.EpisodeNumber;
        ShowTitle = show.DisplayTitle;
        EpisodeLabel = $"S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}";
        EpisodeTitle = episode.Title;
        AirDate = episode.AirDate;
        AirDateDisplay = episode.AirDateDisplay;
        Overview = string.IsNullOrWhiteSpace(episode.Overview)
            ? string.Empty
            : episode.Overview.Trim();
        StillPath = episode.StillPath;
        ShowPosterPath = show.PosterPath;
        StatusLabel = BuildStatusLabel(episode);
        StatusSortRank = GetStatusSortRank(episode);
        IsAvailable = episode.Availability == EpisodeAvailability.Available;
    }

    public long ShowId { get; }

    public int ShowTmdbId { get; }

    public int SeasonNumber { get; }

    public int EpisodeNumber { get; }

    public string ShowTitle { get; }

    public string EpisodeLabel { get; }

    public string EpisodeTitle { get; }

    public DateTime? AirDate { get; }

    public string AirDateDisplay { get; }

    public string Overview { get; }

    public bool HasOverview => !string.IsNullOrWhiteSpace(Overview);

    public string? StillPath { get; }

    public string? ShowPosterPath { get; }

    public string StatusLabel { get; }

    public int StatusSortRank { get; }

    public bool IsAvailable { get; }

    public string DisplayLine => $"{ShowTitle} · {EpisodeLabel} · {AirDateDisplay}";

    [ObservableProperty]
    private ImageSource? stillImage;

    private static string BuildStatusLabel(TrackedEpisode episode)
    {
        if (episode.Availability == EpisodeAvailability.Available)
        {
            return "Available";
        }

        if (!string.IsNullOrWhiteSpace(episode.TorrentState) ||
            !string.IsNullOrWhiteSpace(episode.TorrentHash))
        {
            var normalized = QbittorrentTorrentStateNormalizer.Normalize(
                episode.TorrentState,
                isComplete: false);
            var progress = episode.TorrentProgress > 0
                ? $" ({episode.TorrentProgress:P0})"
                : string.Empty;
            return $"{normalized}{progress}";
        }

        return "Missing";
    }

    private static int GetStatusSortRank(TrackedEpisode episode)
    {
        if (episode.Availability == EpisodeAvailability.Available)
        {
            return 2;
        }

        if (!string.IsNullOrWhiteSpace(episode.TorrentHash) ||
            !string.IsNullOrWhiteSpace(episode.TorrentState))
        {
            return 1;
        }

        return 0;
    }
}
