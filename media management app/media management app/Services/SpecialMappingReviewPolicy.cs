using media_management_app.Models;

namespace media_management_app.Services;

/// <summary>
/// When a pack link must stop at the review window before anything is linked (PARSE-09). Only a special whose file
/// name carries <c>S00Exx</c> and whose own title matches the TMDB title is trusted without review: release groups,
/// western shows above all, often number Season 0 after TVDB rather than TMDB. Every other rule result, every
/// Gemini result, every special left unmapped and every fallback after a failure goes through review.
/// </summary>
public static class SpecialMappingReviewPolicy
{
    public static bool RequiresReview(SpecialMappingResult result) =>
        result.RequiresReview ||
        result.UsedGemini ||
        result.Items.Any(item => !IsTrustedWithoutReview(item));

    public static bool IsTrustedWithoutReview(SpecialMappingItem item) =>
        item.Episode is not null &&
        item.Source == SpecialMappingSource.Rule &&
        item.ProposalReason == SpecialMappingProposalReason.Direct &&
        item.TitleConfirmed;
}
