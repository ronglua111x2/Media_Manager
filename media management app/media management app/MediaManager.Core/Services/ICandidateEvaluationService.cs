using media_management_app.Models;

namespace media_management_app.Services;

public interface ICandidateEvaluationService
{
    RecipeCandidateResult EvaluateEpisode(SearchRecipe recipe, TrackedShow show, TrackedEpisode episode, TorrentSearchResult result);

    RecipeCandidateResult EvaluateMovie(SearchRecipe recipe, TrackedMovie movie, TorrentSearchResult result);
}
