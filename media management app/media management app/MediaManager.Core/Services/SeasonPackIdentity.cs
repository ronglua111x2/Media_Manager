using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public static class SeasonPackIdentity
{
    public const string NoExplicitSeasonCoverage = "no explicit season coverage";

    public static (CandidateRejectReason Reason, string Detail) GetRejectReason(
        TrackedShow show,
        IReadOnlyList<int> selectedSeasons,
        TorrentSearchResult result,
        TorrentCandidateParseResult parsed,
        SearchRecipe packRecipe,
        IReadOnlyList<int>? coveredSeasonsOverride = null)
    {
        var coveredSeasons = coveredSeasonsOverride ?? parsed.CoveredSeasons;
        if (!result.CanAdd)
        {
            return (CandidateRejectReason.NotAddable, $"not addable link type '{result.LinkType}'");
        }

        if (RecipeCandidateFilter.LooksLikePluginError(result.FileName))
        {
            return (CandidateRejectReason.PluginError, "search plugin error row");
        }

        var kind = TorrentReleaseKind.Classify(result.FileName, parsed);
        var kindReject = TorrentReleaseKind.GetRejectReasonForTarget(MediaKind.TvSeasonPack, kind);
        if (kindReject is not null)
        {
            return (CandidateRejectReason.WrongReleaseKind, kindReject);
        }

        if (coveredSeasons.Count == 0)
        {
            return (CandidateRejectReason.EpisodeMismatch, NoExplicitSeasonCoverage);
        }

        if (parsed.ExplicitYear is not null && show.FirstAirYear is not null && parsed.ExplicitYear != show.FirstAirYear)
        {
            return (
                CandidateRejectReason.YearMismatch,
                $"explicit year mismatch {parsed.ExplicitYear} != {show.FirstAirYear}");
        }

        if (!coveredSeasons.Any(selectedSeasons.Contains))
        {
            return (
                CandidateRejectReason.EpisodeMismatch,
                $"does not cover selected seasons {string.Join(",", selectedSeasons)}");
        }

        var titleTokens = TorrentCandidateParser.Tokenize(show.Title).Where(token => token.Length > 2).ToList();
        var matched = titleTokens.Count(token => parsed.TitleTokens.Contains(token, StringComparer.OrdinalIgnoreCase));
        if (matched < Math.Min(2, titleTokens.Count))
        {
            return (CandidateRejectReason.TitleMismatch, "does not contain enough show title tokens");
        }

        return RecipeCandidateFilter.GetRejectReason(packRecipe, result, parsed);
    }
}
