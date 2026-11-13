using media_management_app.Models;

namespace media_management_app.Services;

/// <summary>
/// Plans how the rows of a show follow their TMDB episode id when a refresh renumbers episodes (a recap inserted, a
/// season split, two episodes swapped), the way Sonarr follows TVDB ids (audit L3-09). It only reads; the caller
/// moves the links and rows. It never guesses by title or air date.
/// </summary>
internal static class EpisodeIdentityPlanner
{
    internal sealed record Move(
        TrackedEpisode Row,
        (int SeasonNumber, int EpisodeNumber) From,
        (int SeasonNumber, int EpisodeNumber) To);

    internal sealed class Plan
    {
        /// <summary>Rows that move to the number TMDB now gives their id, with their link and download.</summary>
        public List<Move> Moves { get; } = [];

        /// <summary>
        /// Unused rows sitting on a number a moving row needs (the refresh would overwrite them anyway), or on the number
        /// TMDB now gives the episode of a pinned row.
        /// </summary>
        public HashSet<long> DeleteRowIds { get; } = [];

        /// <summary>
        /// Rows holding a link or download whose episode cannot follow its id now (the id is gone from TMDB, or the move
        /// is blocked). They keep their number, id and metadata, the refresh skips their number and id, and they are
        /// flagged, so the file is never shown as another episode and nothing is downloaded twice.
        /// </summary>
        public Dictionary<long, (TrackedEpisode Row, string Reason)> Pinned { get; } = [];

        public bool IsPinned((int SeasonNumber, int EpisodeNumber) key, int? tmdbEpisodeId) =>
            Pinned.Values.Any(pin =>
                (pin.Row.SeasonNumber, pin.Row.EpisodeNumber) == key ||
                (tmdbEpisodeId is not null && pin.Row.TmdbEpisodeId == tmdbEpisodeId));
    }

    /// <param name="rows">The show's rows before the refresh.</param>
    /// <param name="details">What TMDB returned now.</param>
    /// <param name="holdsDownload">Row has a library link, a torrent or a cart order.</param>
    /// <param name="isInUse">Row holds a download or the user's rating or notes.</param>
    /// <param name="blocked">Old numbers whose links cannot move: their rows stay where they are.</param>
    public static Plan Create(
        IReadOnlyList<TrackedEpisode> rows,
        TmdbShowDetails details,
        Func<TrackedEpisode, bool> holdsDownload,
        Func<TrackedEpisode, bool> isInUse,
        IReadOnlySet<(int SeasonNumber, int EpisodeNumber)> blocked)
    {
        var plan = new Plan();

        // A season TMDB did not return (a partial response) says nothing about its episodes.
        var returnedSeasons = details.Seasons
            .Where(season => season.Episodes.Count > 0)
            .Select(season => season.SeasonNumber)
            .ToHashSet();
        var keysById = details.Seasons
            .SelectMany(season => season.Episodes)
            .Where(episode => episode.TmdbEpisodeId is > 0)
            .GroupBy(episode => episode.TmdbEpisodeId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group.Select(episode => (episode.SeasonNumber, episode.EpisodeNumber)).Distinct().ToList());
        var episodesPerKey = details.Seasons
            .SelectMany(season => season.Episodes)
            .GroupBy(episode => (episode.SeasonNumber, episode.EpisodeNumber))
            .ToDictionary(group => group.Key, group => group.Count());
        var duplicatedIds = rows
            .Where(row => row.TmdbEpisodeId is not null)
            .GroupBy(row => row.TmdbEpisodeId!.Value)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();
        var rowsByKey = rows
            .GroupBy(row => (row.SeasonNumber, row.EpisodeNumber))
            .ToDictionary(group => group.Key, group => group.First());

        var moves = new Dictionary<(int SeasonNumber, int EpisodeNumber), Move>();
        var stranded = new Dictionary<long, (TrackedEpisode Row, string Reason)>();
        foreach (var row in rows)
        {
            // No id yet (first refresh after the update) or an id two rows claim: nothing to follow.
            if (row.TmdbEpisodeId is not { } id || duplicatedIds.Contains(id))
            {
                continue;
            }

            var key = (row.SeasonNumber, row.EpisodeNumber);
            if (!keysById.TryGetValue(id, out var keys))
            {
                // Only a season TMDB returned can tell that an episode is gone; a season it did not return (a failed
                // request, or a season merged into another) proves nothing, but an id found elsewhere is followed.
                if (returnedSeasons.Contains(row.SeasonNumber))
                {
                    stranded[row.Id] = (row, "TMDB no longer lists this episode (deleted, or replaced by a new one)");
                }

                continue;
            }

            if (keys.Contains(key))
            {
                continue;
            }

            if (keys.Count > 1)
            {
                stranded[row.Id] = (row, $"TMDB now lists this episode as {string.Join(" and ", keys.Select(Describe))}");
                continue;
            }

            if (episodesPerKey[keys[0]] > 1)
            {
                stranded[row.Id] = (row, $"TMDB moved it to {Describe(keys[0])}, where it lists more than one episode");
                continue;
            }

            if (blocked.Contains(key))
            {
                stranded[row.Id] = (row, $"TMDB moved it to {Describe(keys[0])}, but its library link could not follow now; it is retried at the next refresh");
                continue;
            }

            moves[key] = new Move(row, key, keys[0]);
        }

        // A move needs its new number free: free already, freed by a row that moves away (chains, swaps), or held by
        // an unused row. A number held by a row in use that stays cancels the move, which can cancel the move into it.
        bool changed;
        do
        {
            changed = false;
            plan.DeleteRowIds.Clear();
            foreach (var move in moves.Values.ToList())
            {
                if (!rowsByKey.TryGetValue(move.To, out var occupant) || moves.ContainsKey(move.To))
                {
                    continue;
                }

                if (!isInUse(occupant))
                {
                    plan.DeleteRowIds.Add(occupant.Id);
                    continue;
                }

                moves.Remove(move.From);
                stranded[move.Row.Id] = (move.Row, $"TMDB moved it to {Describe(move.To)}, which still holds an episode in use");
                changed = true;
            }
        }
        while (changed);

        plan.Moves.AddRange(moves.Values.OrderBy(move => move.From));
        foreach (var (row, reason) in stranded.Values.Where(item => holdsDownload(item.Row)))
        {
            plan.Pinned[row.Id] = (row, reason);
        }

        // A pinned row stands for its episode. An unused row left on the number TMDB now gives that episode would show
        // Missing and Auto-Track would download the episode a second time.
        foreach (var (row, _) in plan.Pinned.Values)
        {
            if (row.TmdbEpisodeId is not { } id || !keysById.TryGetValue(id, out var keys))
            {
                continue;
            }

            foreach (var key in keys)
            {
                if (rowsByKey.TryGetValue(key, out var occupant) &&
                    !plan.Pinned.ContainsKey(occupant.Id) &&
                    !moves.ContainsKey(key) &&
                    !isInUse(occupant))
                {
                    plan.DeleteRowIds.Add(occupant.Id);
                }
            }
        }

        return plan;
    }

    public static string Describe((int SeasonNumber, int EpisodeNumber) key) => $"S{key.SeasonNumber:00}E{key.EpisodeNumber:00}";
}
