using media_management_app.Common;

namespace media_management_app.Models;

public sealed class TrackedShow
{
    public long Id { get; set; }

    public int TmdbId { get; set; }

    public string Title { get; set; } = string.Empty;

    public int? FirstAirYear { get; set; }

    public string? Overview { get; set; }

    public string? PosterPath { get; set; }

    public string? RecipeId { get; set; }

    public string? PackRecipeId { get; set; }

    public string PreferredQuality { get; set; } = "1080p";

    public string PreferredAudioCodec { get; set; } = string.Empty;

    public int MinimumSeeders { get; set; }

    public ShowSeriesStatus SeriesStatus { get; set; } = ShowSeriesStatus.Unknown;

    public int? AutoTrackFromSeason { get; set; }

    public int? AutoTrackFromEpisode { get; set; }

    public string? AutoTrackDownloadFolder { get; set; }

    public bool AutoTrackAutoReconcileAndLink { get; set; } = true;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public string DisplayTitle => FirstAirYear is null ? Title : $"{Title} ({FirstAirYear})";

    public string SeriesStatusLabel => SeriesStatus switch
    {
        ShowSeriesStatus.Ongoing => "Ongoing",
        ShowSeriesStatus.Finished => "Finished",
        _ => "Unknown"
    };

    public int TotalEpisodes { get; set; }

    public int AvailableEpisodes { get; set; }

    public bool IsAutoTracked => AutoTrackFromSeason is not null && AutoTrackFromEpisode is not null;

    public string AutoTrackCheckpointLabel => IsAutoTracked
        ? $"S{AutoTrackFromSeason:00}E{AutoTrackFromEpisode:00}+"
        : string.Empty;
}
