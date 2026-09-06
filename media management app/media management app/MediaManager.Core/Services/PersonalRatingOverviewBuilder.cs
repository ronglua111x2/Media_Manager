using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public static class PersonalRatingOverviewBuilder
{
    public const int HallOfFameSize = 10;
    public const int BillboardHighlightSize = 8;
    public const int MismatchMinEpisodeRatings = 3;

    public static PersonalRatingOverview Build(
        IReadOnlyList<TrackedShow> shows,
        IReadOnlyList<TrackedMovie> movies,
        IReadOnlyList<EpisodeUserRatingRow> episodes,
        Random? shuffle = null)
    {
        var episodeList = episodes.ToList();
        var episodesByShow = episodeList
            .GroupBy(episode => episode.ShowId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var showRatings = shows
            .Select(show => show.Rating)
            .Where(rating => rating.HasValue)
            .Select(rating => rating!.Value)
            .ToList();
        var movieRatings = movies
            .Select(movie => movie.Rating)
            .Where(rating => rating.HasValue)
            .Select(rating => rating!.Value)
            .ToList();
        var episodeRatings = episodeList
            .Where(episode => episode.UserRating.HasValue)
            .Select(episode => episode.UserRating!.Value)
            .ToList();

        var titleRatings = showRatings.Concat(movieRatings).ToList();
        var heatmapRows = BuildHeatmapRows(shows, episodesByShow);
        var (highlights, random) = BuildBillboard(shows, movies, episodeList, shuffle);

        return new PersonalRatingOverview
        {
            ShowCount = shows.Count,
            MovieCount = movies.Count,
            RatedShowCount = showRatings.Count,
            RatedMovieCount = movieRatings.Count,
            EpisodeCount = episodeList.Count,
            RatedEpisodeCount = episodeRatings.Count,
            MeanShowRating = Mean(showRatings),
            MeanMovieRating = Mean(movieRatings),
            MeanEpisodeRating = Mean(episodeRatings),
            WatchStatusStats = BuildWatchStatusStats(shows, movies),
            TitleBands = CountBands(titleRatings),
            EpisodeBands = CountBands(episodeRatings),
            HeatmapRows = heatmapRows,
            RatedShows = BuildRatedTitles(
                shows
                    .Where(show => show.Rating.HasValue)
                    .OrderByDescending(show => show.Rating)
                    .ThenBy(show => show.Title, StringComparer.OrdinalIgnoreCase)
                    .Select(show => (MediaKind.TvEpisode, show.Id, show.TmdbId, show.DisplayTitle, show.PosterPath, show.Rating!.Value, show.WatchStatus))),
            UnratedShowCount = shows.Count(show => show.Rating is null),
            RatedMovies = BuildRatedTitles(
                movies
                    .Where(movie => movie.Rating.HasValue)
                    .OrderByDescending(movie => movie.Rating)
                    .ThenBy(movie => movie.Title, StringComparer.OrdinalIgnoreCase)
                    .Select(movie => (MediaKind.Movie, movie.Id, movie.TmdbId, movie.DisplayTitle, movie.PosterPath, movie.Rating!.Value, movie.WatchStatus))),
            UnratedMovieCount = movies.Count(movie => movie.Rating is null),
            TopEpisodes = BuildHallOfFame(shows, episodeList.Where(episode => !IsSpecials(episode)), descending: true),
            BottomEpisodes = BuildHallOfFame(shows, episodeList.Where(episode => !IsSpecials(episode)), descending: false),
            TopSpecials = BuildHallOfFame(shows, episodeList.Where(IsSpecials), descending: true),
            BottomSpecials = BuildHallOfFame(shows, episodeList.Where(IsSpecials), descending: false),
            Mismatches = BuildMismatches(shows, episodesByShow),
            EmptyOpinions = BuildEmptyOpinions(shows, movies, episodesByShow),
            BillboardHighlights = highlights,
            BillboardRandom = random
        };
    }

    private static IReadOnlyList<ShowHeatmapRow> BuildHeatmapRows(
        IReadOnlyList<TrackedShow> shows,
        IReadOnlyDictionary<long, List<EpisodeUserRatingRow>> episodesByShow)
    {
        var rows = new List<ShowHeatmapRow>();
        foreach (var show in shows.OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase))
        {
            if (!episodesByShow.TryGetValue(show.Id, out var showEpisodes))
            {
                continue;
            }

            var storyEpisodes = showEpisodes.Where(episode => !IsSpecials(episode)).ToList();
            var ratedStoryCount = storyEpisodes.Count(episode => episode.UserRating.HasValue);
            var hasRatedSpecials = showEpisodes.Any(episode => IsSpecials(episode) && episode.UserRating.HasValue);
            if (ratedStoryCount == 0 && !hasRatedSpecials)
            {
                continue;
            }

            var seasons = storyEpisodes
                .GroupBy(episode => episode.SeasonNumber)
                .OrderBy(group => group.Key)
                .Select(group => new HeatmapSeasonGroup
                {
                    SeasonNumber = group.Key,
                    Label = AppConstants.FormatSeasonShortLabel(group.Key),
                    Cells = BuildCells(show.Id, group)
                })
                .ToList();

            var extraCells = showEpisodes
                .Where(episode => IsSpecials(episode) && episode.UserRating.HasValue)
                .OrderBy(episode => episode.EpisodeNumber)
                .ToList();
            IReadOnlyList<HeatmapSeasonGroup> extraSeasons = extraCells.Count == 0
                ? []
                : [
                    new HeatmapSeasonGroup
                    {
                        SeasonNumber = AppConstants.SpecialsSeasonNumber,
                        Label = AppConstants.FormatSeasonShortLabel(AppConstants.SpecialsSeasonNumber),
                        Cells = BuildCells(show.Id, extraCells)
                    }
                ];

            rows.Add(new ShowHeatmapRow
            {
                ShowId = show.Id,
                TmdbId = show.TmdbId,
                Title = show.DisplayTitle,
                PosterPath = show.PosterPath,
                ShowRating = show.Rating,
                ShowBand = EpisodeRatingBandRules.FromRating(show.Rating),
                WatchStatus = show.WatchStatus,
                RatedEpisodeCount = ratedStoryCount,
                EpisodeCount = storyEpisodes.Count,
                Seasons = seasons,
                ExtraSeasons = extraSeasons
            });
        }

        return rows;
    }

    private static IReadOnlyList<HeatmapEpisodeCell> BuildCells(
        long showId,
        IEnumerable<EpisodeUserRatingRow> episodes) =>
        episodes
            .OrderBy(episode => episode.EpisodeNumber)
            .Select(episode => new HeatmapEpisodeCell
            {
                ShowId = showId,
                SeasonNumber = episode.SeasonNumber,
                EpisodeNumber = episode.EpisodeNumber,
                UserRating = episode.UserRating,
                Band = EpisodeRatingBandRules.FromRating(episode.UserRating)
            })
            .ToList();

    private static IReadOnlyList<EpisodeHallOfFameEntry> BuildHallOfFame(
        IReadOnlyList<TrackedShow> shows,
        IEnumerable<EpisodeUserRatingRow> episodes,
        bool descending)
    {
        var titles = shows.ToDictionary(show => show.Id, show => show.DisplayTitle);
        var rated = episodes.Where(episode => episode.UserRating.HasValue);
        var ordered = descending
            ? rated.OrderByDescending(episode => episode.UserRating)
                .ThenBy(episode => titles.GetValueOrDefault(episode.ShowId, string.Empty), StringComparer.OrdinalIgnoreCase)
                .ThenBy(episode => episode.SeasonNumber)
                .ThenBy(episode => episode.EpisodeNumber)
            : rated.OrderBy(episode => episode.UserRating)
                .ThenBy(episode => titles.GetValueOrDefault(episode.ShowId, string.Empty), StringComparer.OrdinalIgnoreCase)
                .ThenBy(episode => episode.SeasonNumber)
                .ThenBy(episode => episode.EpisodeNumber);

        return ordered
            .Take(HallOfFameSize)
            .Select(episode => new EpisodeHallOfFameEntry
            {
                ShowId = episode.ShowId,
                ShowTitle = titles.GetValueOrDefault(episode.ShowId, "Unknown"),
                SeasonNumber = episode.SeasonNumber,
                EpisodeNumber = episode.EpisodeNumber,
                EpisodeTitle = episode.Title,
                UserRating = episode.UserRating!.Value,
                Thought = episode.Thought
            })
            .ToList();
    }

    private static (IReadOnlyList<StatsBillboardItem> Highlights, IReadOnlyList<StatsBillboardItem> Random)
        BuildBillboard(
            IReadOnlyList<TrackedShow> shows,
            IReadOnlyList<TrackedMovie> movies,
            IReadOnlyList<EpisodeUserRatingRow> episodes,
            Random? shuffle)
    {
        var showsById = shows.ToDictionary(show => show.Id);
        var pool = new List<StatsBillboardItem>();

        foreach (var show in shows.Where(item => item.Rating.HasValue))
        {
            pool.Add(FromShow(show));
        }

        foreach (var movie in movies.Where(item => item.Rating.HasValue))
        {
            pool.Add(FromMovie(movie));
        }

        foreach (var episode in episodes.Where(item => item.UserRating.HasValue && !IsSpecials(item)))
        {
            if (!showsById.TryGetValue(episode.ShowId, out var show))
            {
                continue;
            }

            pool.Add(FromEpisode(show, episode));
        }

        var ordered = pool
            .OrderByDescending(item => item.Rating)
            .ThenBy(item => item.Kind)
            .ThenBy(item => item.Headline, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SeasonNumber)
            .ThenBy(item => item.EpisodeNumber)
            .ToList();

        var highlightCount = Math.Min(BillboardHighlightSize, ordered.Count);
        var highlights = ordered.Take(highlightCount).ToList();
        var randomSource = ordered.Count <= BillboardHighlightSize
            ? ordered
            : ordered.Skip(highlightCount).ToList();
        var random = Shuffle(randomSource, shuffle ?? new Random());
        return (highlights, random);
    }

    private static StatsBillboardItem FromShow(TrackedShow show) =>
        new()
        {
            Kind = StatsBillboardKind.Show,
            MediaKind = MediaKind.TvEpisode,
            MediaId = show.Id,
            TmdbId = show.TmdbId,
            PosterPath = show.PosterPath,
            Headline = show.DisplayTitle,
            Rating = show.Rating!.Value,
            Band = EpisodeRatingBandRules.FromRating(show.Rating),
            Thought = show.Thought
        };

    private static StatsBillboardItem FromMovie(TrackedMovie movie) =>
        new()
        {
            Kind = StatsBillboardKind.Movie,
            MediaKind = MediaKind.Movie,
            MediaId = movie.Id,
            TmdbId = movie.TmdbId,
            PosterPath = movie.PosterPath,
            Headline = movie.DisplayTitle,
            Rating = movie.Rating!.Value,
            Band = EpisodeRatingBandRules.FromRating(movie.Rating),
            Thought = movie.Thought
        };

    private static StatsBillboardItem FromEpisode(TrackedShow show, EpisodeUserRatingRow episode) =>
        new()
        {
            Kind = StatsBillboardKind.Episode,
            MediaKind = MediaKind.TvEpisode,
            MediaId = show.Id,
            TmdbId = show.TmdbId,
            PosterPath = show.PosterPath,
            Headline = show.DisplayTitle,
            Subtitle = $"S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00} · {episode.Title}",
            Rating = episode.UserRating!.Value,
            Band = EpisodeRatingBandRules.FromRating(episode.UserRating),
            Thought = episode.Thought,
            SeasonNumber = episode.SeasonNumber,
            EpisodeNumber = episode.EpisodeNumber
        };

    private static IReadOnlyList<StatsBillboardItem> Shuffle(
        IReadOnlyList<StatsBillboardItem> source,
        Random random)
    {
        var list = source.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }

    private static IReadOnlyList<TitleEpisodeMismatch> BuildMismatches(
        IReadOnlyList<TrackedShow> shows,
        IReadOnlyDictionary<long, List<EpisodeUserRatingRow>> episodesByShow)
    {
        var mismatches = new List<TitleEpisodeMismatch>();
        foreach (var show in shows)
        {
            if (show.Rating is null ||
                !episodesByShow.TryGetValue(show.Id, out var showEpisodes))
            {
                continue;
            }

            var rated = showEpisodes
                .Where(episode => episode.UserRating.HasValue)
                .Select(episode => episode.UserRating!.Value)
                .ToList();
            if (rated.Count < MismatchMinEpisodeRatings)
            {
                continue;
            }

            var episodeMean = rated.Average();
            mismatches.Add(new TitleEpisodeMismatch
            {
                ShowId = show.Id,
                Title = show.DisplayTitle,
                ShowRating = show.Rating.Value,
                EpisodeMean = episodeMean,
                Delta = show.Rating.Value - episodeMean,
                RatedEpisodeCount = rated.Count
            });
        }

        return mismatches
            .OrderByDescending(item => Math.Abs(item.Delta))
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<EmptyOpinion> BuildEmptyOpinions(
        IReadOnlyList<TrackedShow> shows,
        IReadOnlyList<TrackedMovie> movies,
        IReadOnlyDictionary<long, List<EpisodeUserRatingRow>> episodesByShow)
    {
        var items = new List<EmptyOpinion>();
        foreach (var show in shows)
        {
            if (show.WatchStatus == UserWatchStatus.Completed && show.Rating is null)
            {
                items.Add(new EmptyOpinion
                {
                    MediaKind = MediaKind.TvEpisode,
                    MediaId = show.Id,
                    Title = show.DisplayTitle,
                    Reason = "Completed, no show rating"
                });
            }

            if (show.Rating is not null)
            {
                episodesByShow.TryGetValue(show.Id, out var showEpisodes);
                var ratedCount = showEpisodes?.Count(episode => episode.UserRating.HasValue) ?? 0;
                if (ratedCount == 0)
                {
                    items.Add(new EmptyOpinion
                    {
                        MediaKind = MediaKind.TvEpisode,
                        MediaId = show.Id,
                        Title = show.DisplayTitle,
                        Reason = "Show rated, no episode ratings"
                    });
                }
            }
        }

        foreach (var movie in movies)
        {
            if (movie.WatchStatus == UserWatchStatus.Completed && movie.Rating is null)
            {
                items.Add(new EmptyOpinion
                {
                    MediaKind = MediaKind.Movie,
                    MediaId = movie.Id,
                    Title = movie.DisplayTitle,
                    Reason = "Completed, no rating"
                });
            }
        }

        return items
            .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<PersonalRatingBandCount> CountBands(IReadOnlyList<double> ratings)
    {
        var counts = ratings
            .GroupBy(rating => EpisodeRatingBandRules.FromRating(rating))
            .ToDictionary(group => group.Key, group => group.Count());

        return EpisodeRatingBandRules.RatedBands
            .Select(entry => new PersonalRatingBandCount
            {
                Band = entry.Band,
                Label = entry.Label,
                Count = counts.GetValueOrDefault(entry.Band)
            })
            .ToList();
    }

    private static IReadOnlyList<TitleRatingCard> BuildRatedTitles(
        IEnumerable<(MediaKind Kind, long Id, int TmdbId, string Title, string? PosterPath, double Rating, UserWatchStatus WatchStatus)> titles) =>
        titles
            .Select(title => new TitleRatingCard
            {
                MediaKind = title.Kind,
                MediaId = title.Id,
                TmdbId = title.TmdbId,
                Title = title.Title,
                PosterPath = title.PosterPath,
                Rating = title.Rating,
                Band = EpisodeRatingBandRules.FromRating(title.Rating),
                WatchStatus = title.WatchStatus
            })
            .ToList();

    private static IReadOnlyList<WatchStatusStat> BuildWatchStatusStats(
        IReadOnlyList<TrackedShow> shows,
        IReadOnlyList<TrackedMovie> movies)
    {
        return new[]
        {
            UserWatchStatus.Watching,
            UserWatchStatus.Completed,
            UserWatchStatus.OnHold,
            UserWatchStatus.Dropped,
            UserWatchStatus.PlanToWatch
        }
            .Select(status => new WatchStatusStat
            {
                Status = status,
                Label = TrackedShow.FormatWatchStatusLabel(status),
                ShowCount = shows.Count(show => show.WatchStatus == status),
                MovieCount = movies.Count(movie => movie.WatchStatus == status)
            })
            .Where(stat => stat.ShowCount > 0 || stat.MovieCount > 0)
            .ToList();
    }

    private static bool IsSpecials(EpisodeUserRatingRow episode) =>
        episode.SeasonNumber == AppConstants.SpecialsSeasonNumber;

    private static double? Mean(IReadOnlyList<double> values) =>
        values.Count == 0 ? null : values.Average();
}
