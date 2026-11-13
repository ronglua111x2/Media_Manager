using media_management_app.Models;

namespace media_management_app.Services;

public interface ISearchPlanBuilder
{
    IReadOnlyList<string> BuildEpisodeQueries(SearchRecipe recipe, TrackedShow show, TrackedEpisode episode);

    IReadOnlyList<string> BuildMovieQueries(SearchRecipe recipe, TrackedMovie movie);

    IReadOnlyList<string> BuildShowSnapshotQueries(SearchRecipe recipe, TrackedShow show);

    /// <summary>
    /// Targeted season-pack queries for the selected seasons (native-search P4, audit SRCH-03): the recipe's
    /// own {season} templates rendered per season, or "{title} S{season:00}" / "{title} season {season}".
    /// Measured: "Rick and Morty S03 1080p" found 111 S03 packs, the show-only snapshot query 7.
    /// </summary>
    IReadOnlyList<string> BuildSeasonPackQueries(SearchRecipe recipe, TrackedShow show, IReadOnlyList<int> seasons);
}
