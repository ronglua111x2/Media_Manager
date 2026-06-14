using media_management_app.Models;

namespace media_management_app.ViewModels;

public sealed class AutoTrackNewEpisodeViewModel
{
    public AutoTrackNewEpisodeViewModel(TrackedShow show, TrackedEpisode episode)
    {
        ShowTitle = show.DisplayTitle;
        EpisodeLabel = $"S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}";
        EpisodeTitle = episode.Title;
        AirDateDisplay = episode.AirDateDisplay;
        PosterPath = show.PosterPath;
    }

    public string ShowTitle { get; }

    public string EpisodeLabel { get; }

    public string EpisodeTitle { get; }

    public string AirDateDisplay { get; }

    public string? PosterPath { get; }

    public string DisplayLine => $"{ShowTitle} · {EpisodeLabel} · {AirDateDisplay}";
}
