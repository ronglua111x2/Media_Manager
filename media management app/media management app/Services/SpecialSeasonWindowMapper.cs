using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

/// <summary>TMDB Season 0 entries that aired inside one regular season's air-date window.</summary>
public sealed record SpecialSeasonBlock(int SeasonNumber, DateTime SeasonStart, IReadOnlyList<TrackedEpisode> Entries);

/// <summary>A candidate the window mapper resolved. <see cref="IsInferred"/>: an unnumbered file put in the one free slot.</summary>
public sealed record SpecialWindowAssignment(int CandidateIndex, TrackedEpisode Episode, bool IsInferred);

public sealed class SpecialWindowMapping
{
    public IReadOnlyList<SpecialWindowAssignment> Assignments { get; init; } = [];

    /// <summary>"k-th file → k-th entry" for blocks that are not exact. Shown for review only, never applied.</summary>
    public IReadOnlyDictionary<int, TrackedEpisode> Suggestions { get; init; } = new Dictionary<int, TrackedEpisode>();

    /// <summary>Why a candidate of a season block was not resolved, by candidate index.</summary>
    public IReadOnlyDictionary<int, string> UnresolvedReasons { get; init; } = new Dictionary<int, string>();
}

/// <summary>
/// Deterministic special/OVA tier (parser audit 10.3 step 1, tightened by 10.4). TMDB S00 is split into blocks by
/// each regular season's air-date window: an entry belongs to season n when it aired on or after season n's first
/// episode and before the next season's. Entries that aired before season 1 join season 1; entries without an air
/// date are left out. Inside a block, entries are ordered by TMDB episode number (not air date).
/// A season's special files (<c>SnSnn</c>, <c>Season n/Specials</c>, OVA files in a season folder) map k-th → k-th
/// only when the block holds exactly as many entries as there are files, the numbered files are exactly 1..m and
/// at most one file is unnumbered (that one takes the remaining slot and is marked inferred). Anything else is
/// left for Gemini or review: on real TMDB lists "files ≤ entries" was wrong on 2 of 8 shows.
/// </summary>
public static class SpecialSeasonWindowMapper
{
    public static IReadOnlyList<SpecialSeasonBlock> BuildBlocks(
        IReadOnlyList<TrackedEpisode> regularEpisodes,
        IReadOnlyList<TrackedEpisode> specialsEpisodes)
    {
        var starts = regularEpisodes
            .Where(episode => episode.SeasonNumber > 0 && episode.AirDate is not null)
            .GroupBy(episode => episode.SeasonNumber)
            .Select(group => (Season: group.Key, Start: group.Min(episode => episode.AirDate!.Value.Date)))
            .OrderBy(item => item.Start)
            .ThenBy(item => item.Season)
            .ToList();
        if (starts.Count == 0)
        {
            return [];
        }

        var entries = starts.ToDictionary(item => item.Season, _ => new List<TrackedEpisode>());
        foreach (var special in specialsEpisodes.Where(episode =>
                     episode.SeasonNumber == AppConstants.SpecialsSeasonNumber && episode.AirDate is not null))
        {
            var airDate = special.AirDate!.Value.Date;
            var owner = starts[0].Season;
            foreach (var (season, start) in starts)
            {
                if (airDate >= start)
                {
                    owner = season;
                }
            }

            entries[owner].Add(special);
        }

        return starts
            .Select(item => new SpecialSeasonBlock(
                item.Season,
                item.Start,
                entries[item.Season].OrderBy(episode => episode.EpisodeNumber).ToList()))
            .OrderBy(block => block.SeasonNumber)
            .ToList();
    }

    public static SpecialWindowMapping Map(
        IReadOnlyList<SpecialMappingCandidate> candidates,
        IReadOnlyList<SpecialSeasonBlock> blocks,
        IReadOnlySet<int> claimedCandidates,
        IReadOnlySet<int> claimedEpisodes)
    {
        if (blocks.Count == 0 || candidates.Count == 0)
        {
            return new SpecialWindowMapping();
        }

        int? onlySeason = blocks.Count == 1 ? blocks[0].SeasonNumber : null;
        var assignments = new List<SpecialWindowAssignment>();
        var suggestions = new Dictionary<int, TrackedEpisode>();
        var unresolved = new Dictionary<int, string>();

        var groups = candidates
            .Select((candidate, index) => (Candidate: candidate, Index: index))
            .Where(item => !claimedCandidates.Contains(item.Index))
            .Where(item => item.Candidate.NamingPattern != SpecialMappingNamingPattern.StandardS00E)
            .Select(item => (item.Candidate, item.Index, Season: item.Candidate.ParentSeason is > 0 ? item.Candidate.ParentSeason : onlySeason))
            .Where(item => item.Season is > 0)
            .GroupBy(item => item.Season!.Value);

        foreach (var group in groups)
        {
            var block = blocks.FirstOrDefault(item => item.SeasonNumber == group.Key);
            var files = group.ToList();
            if (block is null)
            {
                foreach (var file in files)
                {
                    unresolved[file.Index] = $"Season {group.Key} has no aired TMDB episodes to place its specials.";
                }

                continue;
            }

            var slots = block.Entries.Where(entry => !claimedEpisodes.Contains(entry.EpisodeNumber)).ToList();
            var numbered = files
                .Where(file => file.Candidate.LocalIndex > 0)
                .OrderBy(file => file.Candidate.LocalIndex)
                .ThenBy(file => file.Candidate.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var unnumbered = files
                .Where(file => file.Candidate.LocalIndex <= 0)
                .OrderBy(file => file.Candidate.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var numbersAreOneToM = numbered
                .Select(file => file.Candidate.LocalIndex)
                .SequenceEqual(Enumerable.Range(1, numbered.Count));

            if (files.Count == slots.Count && unnumbered.Count <= 1 && numbersAreOneToM)
            {
                foreach (var file in numbered)
                {
                    assignments.Add(new SpecialWindowAssignment(file.Index, slots[file.Candidate.LocalIndex - 1], IsInferred: false));
                }

                foreach (var file in unnumbered)
                {
                    assignments.Add(new SpecialWindowAssignment(file.Index, slots[numbered.Count], IsInferred: true));
                }

                continue;
            }

            var reason = DescribeMismatch(group.Key, files.Count, slots.Count, unnumbered.Count, numbersAreOneToM);
            var ordered = numbered.Concat(unnumbered).ToList();
            for (var position = 0; position < ordered.Count; position++)
            {
                unresolved[ordered[position].Index] = reason;
                if (numbersAreOneToM && ordered.Count <= slots.Count)
                {
                    suggestions[ordered[position].Index] = slots[position];
                }
            }
        }

        return new SpecialWindowMapping
        {
            Assignments = assignments,
            Suggestions = suggestions,
            UnresolvedReasons = unresolved
        };
    }

    private static string DescribeMismatch(int season, int files, int slots, int unnumbered, bool numbersAreOneToM)
    {
        if (!numbersAreOneToM)
        {
            return $"Season {season} special numbers are not a clean 1..n run; not mapped automatically.";
        }

        if (files != slots)
        {
            return $"Season {season} window has {slots} TMDB special(s) for {files} file(s); not mapped automatically.";
        }

        return $"Season {season} has {unnumbered} unnumbered special files; not mapped automatically.";
    }
}
