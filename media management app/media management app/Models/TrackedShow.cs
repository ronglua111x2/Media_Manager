using System.Text.Json;
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

    public string? AlternativeTitlesJson { get; set; }

    public string? ExcludedAlternativeTitlesJson { get; set; }

    /// <summary>Null = default TMDB seasons; non-null = TMDB episode group id used for S/E organization.</summary>
    public string? EpisodeGroupId { get; set; }

    /// <summary>Cached display name for <see cref="EpisodeGroupId"/>.</summary>
    public string? EpisodeGroupName { get; set; }

    public string? RecipeId { get; set; }

    public string? PackRecipeId { get; set; }

    public string PreferredQuality { get; set; } = "1080p";

    public string PreferredAudioCodec { get; set; } = string.Empty;

    public int MinimumSeeders { get; set; }

    public ShowSeriesStatus SeriesStatus { get; set; } = ShowSeriesStatus.Unknown;

    public UserWatchStatus WatchStatus { get; set; } = UserWatchStatus.None;

    public int WatchedEpisodes { get; set; }

    /// <summary>TMDB planned regular-season episode total (excludes specials when populated from seasons[].episode_count).</summary>
    public int PlannedEpisodeCount { get; set; }

    public int? AutoTrackFromSeason { get; set; }

    public int? AutoTrackFromEpisode { get; set; }

    public string? AutoTrackDownloadFolder { get; set; }

    public bool AutoTrackAutoReconcileAndLink { get; set; } = true;

    public DayOfWeek? AutoTrackAnchorDayOfWeek { get; set; }

    public string? AutoTrackAnchorTimeLocal { get; set; }

    public string? AutoTrackLastTmdbWeekKey { get; set; }

    /// <summary>Local timestamp of the last TMDB refresh for auto-track (null = never refreshed under this field).</summary>
    public DateTime? AutoTrackLastTmdbRefreshLocal { get; set; }

    public AutoTrackTmdbState AutoTrackTmdbState { get; set; } = AutoTrackTmdbState.Active;

    public string? AutoTrackMinQuality { get; set; }

    public int? AutoTrackMinSeeders { get; set; }

    public int? AutoTrackMinFileSizeMb { get; set; }

    public int? AutoTrackMaxFileSizeMb { get; set; }

    public string? AutoTrackAllowedQualities { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public string DisplayTitle => FirstAirYear is null ? Title : $"{Title} ({FirstAirYear})";

    public bool UsesEpisodeGroup => !string.IsNullOrWhiteSpace(EpisodeGroupId);

    public string EpisodeOrganizationLabel => UsesEpisodeGroup
        ? (string.IsNullOrWhiteSpace(EpisodeGroupName) ? "Episode Group" : EpisodeGroupName!)
        : "Default (TMDB seasons)";

    public string SeriesStatusLabel => SeriesStatus switch
    {
        ShowSeriesStatus.Ongoing => "Ongoing",
        ShowSeriesStatus.Finished => "Finished",
        _ => "Unknown"
    };

    public string WatchStatusLabel => FormatWatchStatusLabel(WatchStatus);

    public static string FormatWatchStatusLabel(UserWatchStatus status) => status switch
    {
        UserWatchStatus.Watching => "Watching",
        UserWatchStatus.Completed => "Completed",
        UserWatchStatus.OnHold => "On-Hold",
        UserWatchStatus.Dropped => "Dropped",
        UserWatchStatus.PlanToWatch => "Plan to Watch",
        _ => "Unset"
    };

    public int TotalEpisodes { get; set; }

    public int AvailableEpisodes { get; set; }

    /// <summary>Denominator for watch progress: max(planned from TMDB, synced episode rows).</summary>
    public int WatchEpisodeTotal => Math.Max(PlannedEpisodeCount, TotalEpisodes);

    public bool IsAutoTracked => AutoTrackFromSeason is not null && AutoTrackFromEpisode is not null;

    public string AutoTrackCheckpointLabel => IsAutoTracked
        ? $"S{AutoTrackFromSeason:00}E{AutoTrackFromEpisode:00}+"
        : string.Empty;

    public IReadOnlyList<string> AlternativeTitles => ParseAlternativeTitles(AlternativeTitlesJson);

    public IReadOnlyList<string> ExcludedFromSearchAlternativeTitles =>
        ParseAlternativeTitles(ExcludedAlternativeTitlesJson);

    public IReadOnlyList<string> GetSearchableAlternativeTitles() =>
        GetSearchableAlternativeTitles(AlternativeTitles, ExcludedFromSearchAlternativeTitles);

    public static IReadOnlyList<string> GetSearchableAlternativeTitles(
        IReadOnlyList<string> alternativeTitles,
        IReadOnlyList<string> excludedFromSearchAlternativeTitles)
    {
        if (excludedFromSearchAlternativeTitles.Count == 0)
        {
            return alternativeTitles;
        }

        var excluded = new HashSet<string>(excludedFromSearchAlternativeTitles, StringComparer.OrdinalIgnoreCase);
        return alternativeTitles
            .Where(title => !string.IsNullOrWhiteSpace(title) && !excluded.Contains(title.Trim()))
            .ToList();
    }

    public static IReadOnlyList<string> ParseAlternativeTitles(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string? SerializeAlternativeTitles(IEnumerable<string> titles)
    {
        var normalized = titles
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Select(title => title.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return normalized.Count == 0 ? null : JsonSerializer.Serialize(normalized);
    }
}
