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

    public DayOfWeek? AutoTrackAnchorDayOfWeek { get; set; }

    public string? AutoTrackAnchorTimeLocal { get; set; }

    public string? AutoTrackLastTmdbWeekKey { get; set; }

    public AutoTrackTmdbState AutoTrackTmdbState { get; set; } = AutoTrackTmdbState.Active;

    public string? AutoTrackMinQuality { get; set; }

    public int? AutoTrackMinSeeders { get; set; }

    public int? AutoTrackMinFileSizeMb { get; set; }

    public int? AutoTrackMaxFileSizeMb { get; set; }

    public string? AutoTrackAllowedQualities { get; set; }

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
