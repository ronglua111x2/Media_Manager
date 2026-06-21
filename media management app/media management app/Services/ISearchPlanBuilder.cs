using media_management_app.Models;

namespace media_management_app.Services;

public interface ISearchPlanBuilder
{
    IReadOnlyList<string> BuildEpisodeQueries(SearchRecipe recipe, TrackedShow show, TrackedEpisode episode);

    IReadOnlyList<string> BuildMovieQueries(SearchRecipe recipe, TrackedMovie movie);

    IReadOnlyList<string> BuildShowSnapshotQueries(SearchRecipe recipe, TrackedShow show);
}
