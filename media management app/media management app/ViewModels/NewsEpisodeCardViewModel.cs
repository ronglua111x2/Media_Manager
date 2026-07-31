using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.ViewModels;

public sealed partial class NewsEpisodeCardViewModel : ObservableObject
{
    public NewsEpisodeCardViewModel(TrackedShow show, TrackedEpisode episode)
    {
        ShowTmdbId = show.TmdbId;
        SeasonNumber = episode.SeasonNumber;
        EpisodeNumber = episode.EpisodeNumber;
        ShowTitle = show.DisplayTitle;
        EpisodeLabel = $"S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}";
        EpisodeTitle = episode.Title;
        AirDateDisplay = episode.AirDateDisplay;
        Overview = string.IsNullOrWhiteSpace(episode.Overview)
            ? string.Empty
            : episode.Overview.Trim();
        StillPath = episode.StillPath;
        ShowPosterPath = show.PosterPath;
        StatusLabel = BuildStatusLabel(episode);
        IsAvailable = episode.Availability == EpisodeAvailability.Available;
    }

    public int ShowTmdbId { get; }

    public int SeasonNumber { get; }

    public int EpisodeNumber { get; }

    public string ShowTitle { get; }

    public string EpisodeLabel { get; }

    public string EpisodeTitle { get; }

    public string AirDateDisplay { get; }

    public string Overview { get; }

    public bool HasOverview => !string.IsNullOrWhiteSpace(Overview);

    public string? StillPath { get; }

    public string? ShowPosterPath { get; }

    public string StatusLabel { get; }

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

        if (!string.IsNullOrWhiteSpace(episode.TorrentState))
        {
            var progress = episode.TorrentProgress > 0
                ? $" ({episode.TorrentProgress:P0})"
                : string.Empty;
            return $"{episode.TorrentState}{progress}";
        }

        if (!string.IsNullOrWhiteSpace(episode.TorrentHash))
        {
            return "Downloading";
        }

        return "Missing";
    }
}
