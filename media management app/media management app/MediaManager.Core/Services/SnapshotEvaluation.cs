using System.Collections.Concurrent;
using media_management_app.Models;

namespace media_management_app.Services;

/// <summary>
/// Scores a show-level snapshot against the episodes it was taken for. One snapshot search serves every wanted
/// episode of a show, so a row is judged against each target and the best verdict wins: it counts as "good" when it
/// is good for at least one wanted episode. Used for the native-vs-qBittorrent compare log (Both mode), where the
/// snapshot used to be logged as "evaluated=false" and the P6 numbers for TV/anime stayed empty.
/// </summary>
public static class SnapshotEvaluation
{
    /// <summary>
    /// Combines per-target evaluators into one: accepted beats rejected, then the higher <see cref="CandidateTier"/>,
    /// then the higher recipe score. Answers are remembered per listing, because the compare log asks about the
    /// native rows and the qBittorrent rows and a listing is the same torrent whichever side found it.
    /// </summary>
    public static Func<TorrentSearchResult, RecipeCandidateResult> BestOf(
        SearchRecipe recipe,
        IReadOnlyList<Func<TorrentSearchResult, RecipeCandidateResult>> perTarget)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (perTarget.Count == 0)
        {
            throw new ArgumentException("At least one target is needed.", nameof(perTarget));
        }

        var memo = new ConcurrentDictionary<string, RecipeCandidateResult>(StringComparer.Ordinal);
        return row => memo.GetOrAdd($"{row.FileUrl}\n{row.FileName}", _ => Best(recipe, perTarget, row));
    }

    private static RecipeCandidateResult Best(
        SearchRecipe recipe,
        IReadOnlyList<Func<TorrentSearchResult, RecipeCandidateResult>> perTarget,
        TorrentSearchResult row)
    {
        RecipeCandidateResult? best = null;
        var bestTier = CandidateTier.Rejected;
        foreach (var evaluate in perTarget)
        {
            var result = evaluate(row);
            var tier = CandidateTiers.Classify(recipe, result);
            if (best is null ||
                (result.IsAccepted && !best.IsAccepted) ||
                (result.IsAccepted && best.IsAccepted && (tier > bestTier || (tier == bestTier && result.TotalScore > best.TotalScore))))
            {
                best = result;
                bestTier = tier;
            }
        }

        return best!;
    }
}
