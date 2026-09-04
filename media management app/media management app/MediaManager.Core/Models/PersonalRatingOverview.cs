using media_management_app.Common;

namespace media_management_app.Models;

public sealed class PersonalRatingOverview
{
    public int ShowCount { get; init; }

    public int MovieCount { get; init; }

    public int RatedShowCount { get; init; }

    public int RatedMovieCount { get; init; }

    public int EpisodeCount { get; init; }

    public int RatedEpisodeCount { get; init; }

    public double? MeanShowRating { get; init; }

    public double? MeanMovieRating { get; init; }

    public double? MeanEpisodeRating { get; init; }

    public IReadOnlyList<WatchStatusStat> WatchStatusStats { get; init; } = [];

    public IReadOnlyList<PersonalRatingBandCount> TitleBands { get; init; } = [];

    public IReadOnlyList<PersonalRatingBandCount> EpisodeBands { get; init; } = [];

    public IReadOnlyList<ShowHeatmapRow> HeatmapRows { get; init; } = [];

    public IReadOnlyList<TitleRatingCard> RatedShows { get; init; } = [];

    public int UnratedShowCount { get; init; }

    public IReadOnlyList<TitleRatingCard> RatedMovies { get; init; } = [];

    public int UnratedMovieCount { get; init; }

    public IReadOnlyList<EpisodeHallOfFameEntry> TopEpisodes { get; init; } = [];

    public IReadOnlyList<EpisodeHallOfFameEntry> BottomEpisodes { get; init; } = [];

    public IReadOnlyList<EpisodeHallOfFameEntry> TopSpecials { get; init; } = [];

    public IReadOnlyList<EpisodeHallOfFameEntry> BottomSpecials { get; init; } = [];

    public IReadOnlyList<TitleEpisodeMismatch> Mismatches { get; init; } = [];

    public IReadOnlyList<EmptyOpinion> EmptyOpinions { get; init; } = [];

    public bool HasLibrary => ShowCount > 0 || MovieCount > 0;
}

public sealed class WatchStatusStat
{
    public UserWatchStatus Status { get; init; }

    public string Label { get; init; } = string.Empty;

    public int ShowCount { get; init; }

    public int MovieCount { get; init; }

    public string SplitText
    {
        get
        {
            var shows = ShowCount == 1 ? "1 show" : $"{ShowCount} shows";
            var movies = MovieCount == 1 ? "1 movie" : $"{MovieCount} movies";
            return $"{shows} · {movies}";
        }
    }
}

public sealed class PersonalRatingBandCount
{
    public EpisodeRatingBand Band { get; init; }

    public string Label { get; init; } = string.Empty;

    public int Count { get; init; }
}

public sealed class ShowHeatmapRow
{
    public long ShowId { get; init; }

    public int TmdbId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string? PosterPath { get; init; }

    public double? ShowRating { get; init; }

    public EpisodeRatingBand ShowBand { get; init; }

    public UserWatchStatus WatchStatus { get; init; }

    public bool HasWatchStatus => WatchStatus != UserWatchStatus.None;

    public string WatchStatusLabel => TrackedShow.FormatWatchStatusLabel(WatchStatus);

    public int RatedEpisodeCount { get; init; }

    public int EpisodeCount { get; init; }

    public IReadOnlyList<HeatmapSeasonGroup> Seasons { get; init; } = [];

    public IReadOnlyList<HeatmapSeasonGroup> ExtraSeasons { get; init; } = [];
}

public sealed class HeatmapSeasonGroup
{
    public int SeasonNumber { get; init; }

    public string Label { get; init; } = string.Empty;

    public IReadOnlyList<HeatmapEpisodeCell> Cells { get; init; } = [];
}

public sealed class HeatmapEpisodeCell
{
    public long ShowId { get; init; }

    public int SeasonNumber { get; init; }

    public int EpisodeNumber { get; init; }

    public double? UserRating { get; init; }

    public EpisodeRatingBand Band { get; init; }

    public bool HasRating => UserRating.HasValue;

    public string Label => $"E{EpisodeNumber}";

    public string ScoreText => UserRating is { } rating
        ? rating.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
        : "N/A";
}

public sealed class TitleRatingCard
{
    public MediaKind MediaKind { get; init; }

    public long MediaId { get; init; }

    public int TmdbId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string? PosterPath { get; init; }

    public double Rating { get; init; }

    public EpisodeRatingBand Band { get; init; }

    public UserWatchStatus WatchStatus { get; init; }

    public bool HasWatchStatus => WatchStatus != UserWatchStatus.None;

    public string WatchStatusLabel => TrackedShow.FormatWatchStatusLabel(WatchStatus);
}

public sealed class EpisodeHallOfFameEntry
{
    public long ShowId { get; init; }

    public string ShowTitle { get; init; } = string.Empty;

    public int SeasonNumber { get; init; }

    public int EpisodeNumber { get; init; }

    public string EpisodeTitle { get; init; } = string.Empty;

    public double UserRating { get; init; }

    public string? Thought { get; init; }

    public string EpisodeCode => $"S{SeasonNumber:00}E{EpisodeNumber:00}";
}

public sealed class TitleEpisodeMismatch
{
    public long ShowId { get; init; }

    public string Title { get; init; } = string.Empty;

    public double ShowRating { get; init; }

    public double EpisodeMean { get; init; }

    public double Delta { get; init; }

    public int RatedEpisodeCount { get; init; }

    public string DeltaText =>
        Delta >= 0
            ? $"+{Delta.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}"
            : Delta.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class EmptyOpinion
{
    public MediaKind MediaKind { get; init; }

    public long MediaId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;
}
