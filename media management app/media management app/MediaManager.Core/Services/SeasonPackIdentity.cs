using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public static class SeasonPackIdentity
{
    public const string NoExplicitSeasonCoverage = "no explicit season coverage";

    private static readonly SearchTitleResolver TitleResolver = new();

    /// <param name="titleVariants">
    /// Titles the pack may carry (primary, recipe aliases, library alternative titles: romaji, original title...).
    /// Defaults to <see cref="SearchTitleResolver.ResolveForMatching"/>, the same set the episode evaluator uses: a pack
    /// named only "Yagate Kimi ni Naru S01" or "Honzuki no Gekokujou S04" is the show's pack.
    /// </param>
    public static (CandidateRejectReason Reason, string Detail) GetRejectReason(
        TrackedShow show,
        IReadOnlyList<int> selectedSeasons,
        TorrentSearchResult result,
        TorrentCandidateParseResult parsed,
        SearchRecipe packRecipe,
        IReadOnlyList<int>? coveredSeasonsOverride = null,
        IReadOnlyList<string>? titleVariants = null)
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

        titleVariants ??= TitleResolver.ResolveForMatching(packRecipe, show.Title, show.GetSearchableAlternativeTitles());
        if (!MatchesTitle(titleVariants, parsed.TitleTokens))
        {
            return (CandidateRejectReason.TitleMismatch, "does not contain enough show title or alias tokens");
        }

        return RecipeCandidateFilter.GetRejectReason(packRecipe, result, parsed);
    }

    /// <summary>
    /// True when the release title carries one title variant: at least two of its words (all of them for a one-word
    /// title), a repeated word counted only as often as the release repeats it ("Hunter x Hunter" is not matched by
    /// "Monster Hunter Stories"). A title without a word longer than two letters ("86") must start the release title.
    /// </summary>
    public static bool MatchesTitle(IReadOnlyList<string> titleVariants, IReadOnlyList<string> candidateTokens)
    {
        foreach (var variant in titleVariants.Where(title => !string.IsNullOrWhiteSpace(title)))
        {
            var titleTokens = TitleNormalizer.SignificantTitleTokens(variant, distinct: false);
            if (titleTokens.Count == 0)
            {
                var exact = TitleNormalizer.MatchTokens(variant);
                var candidate = candidateTokens.Select(TitleNormalizer.NormalizeToken).Where(token => token.Length > 0).ToList();
                if (exact.Count > 0 && candidate.Count >= exact.Count && candidate.Take(exact.Count).SequenceEqual(exact, StringComparer.Ordinal))
                {
                    return true;
                }

                continue;
            }

            if (TitleNormalizer.CountTitleTokenMatches(titleTokens, candidateTokens) >= Math.Min(2, titleTokens.Count))
            {
                return true;
            }
        }

        return false;
    }
}
