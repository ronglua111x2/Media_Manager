using System.Text.RegularExpressions;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

/// <summary>TMDB Season 0 entries that aired inside one regular season's air-date window.</summary>
public sealed record SpecialSeasonBlock(int SeasonNumber, DateTime SeasonStart, IReadOnlyList<TrackedEpisode> Entries);

/// <summary>A candidate the window mapper resolved. <see cref="IsInferred"/>: an unnumbered file put in the one free slot.</summary>
public sealed record SpecialWindowAssignment(int CandidateIndex, TrackedEpisode Episode, bool IsInferred);

/// <summary>A candidate <see cref="SpecialSeasonWindowMapper.MapByKind"/> resolved, and which filter placed it.</summary>
public sealed record SpecialKindAssignment(int CandidateIndex, TrackedEpisode Episode, SpecialMappingProposalReason Reason);

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
    private static readonly Regex OvaTitleRegex = new(
        @"\b(?:OVA|OAD)s?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// TMDB titles of entries that are not part of a numbered special series: recaps (also "Recapping ..."), digests,
    /// previews, promos, commentaries, interviews, trailers, teasers, PVs and CMs. Kept short on purpose: an entry it
    /// misses only keeps the count from matching, which leaves the files for Gemini and review.
    /// </summary>
    private static readonly Regex AsideTitleRegex = new(
        @"\b(?:recap\w*|digest|previews?|promo\w*|commentary|interviews?|trailers?|teasers?|PV|CM)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

    /// <summary>
    /// Second pass for the season groups the exact-count pass could not place (a recap or promo sits inside the
    /// window, or the pack holds only the OVAs): the same "k-th file → k-th entry" over a filtered window.
    /// <list type="bullet">
    /// <item>OVA files (<c>OVA - N</c>, or OVA/OAD in the name) count only the entries whose TMDB title names an
    /// OVA/OAD, when the window has any (<see cref="SpecialMappingProposalReason.OvaOrder"/>).</item>
    /// <item>Otherwise the window without recap/preview/promo-type titles
    /// (<see cref="SpecialMappingProposalReason.WindowWithoutAsides"/>), only when that left at least one out.</item>
    /// </list>
    /// As in the first pass: the numbered files of the season must be exactly 1..m and the filtered window must hold
    /// exactly m entries (entries ordered by TMDB number). Entries another rule gave to a file outside the group are
    /// left out first; a group file another rule already placed must sit on its own slot, or the whole group is
    /// left open. Unnumbered files are never placed here. Seasons only: a root folder counts only when the show has
    /// one season (as in <see cref="Map"/>).
    /// </summary>
    /// <param name="placed">Candidate index → S00 episode, for every pair the earlier rules proposed.</param>
    public static IReadOnlyList<SpecialKindAssignment> MapByKind(
        IReadOnlyList<SpecialMappingCandidate> candidates,
        IReadOnlyList<SpecialSeasonBlock> blocks,
        IReadOnlyDictionary<int, int> placed)
    {
        if (blocks.Count == 0 || candidates.Count == 0)
        {
            return [];
        }

        int? onlySeason = blocks.Count == 1 ? blocks[0].SeasonNumber : null;
        var assignments = new List<SpecialKindAssignment>();
        var groups = candidates
            .Select((candidate, index) => (Candidate: candidate, Index: index))
            .Where(item => item.Candidate.NamingPattern != SpecialMappingNamingPattern.StandardS00E)
            .Select(item => (item.Candidate, item.Index, Season: item.Candidate.ParentSeason is > 0 ? item.Candidate.ParentSeason : onlySeason))
            .Where(item => item.Season is > 0)
            .GroupBy(item => item.Season!.Value);

        foreach (var group in groups)
        {
            var block = blocks.FirstOrDefault(item => item.SeasonNumber == group.Key);
            var numbered = group
                .Where(item => item.Candidate.LocalIndex > 0)
                .OrderBy(item => item.Candidate.LocalIndex)
                .ToList();
            if (block is null ||
                numbered.Count == 0 ||
                !numbered.Select(item => item.Candidate.LocalIndex).SequenceEqual(Enumerable.Range(1, numbered.Count)) ||
                numbered.All(item => placed.ContainsKey(item.Index)))
            {
                continue;
            }

            var groupIndices = numbered.Select(item => item.Index).ToHashSet();
            var takenElsewhere = placed
                .Where(pair => !groupIndices.Contains(pair.Key))
                .Select(pair => pair.Value)
                .ToHashSet();
            var open = block.Entries.Where(entry => !takenElsewhere.Contains(entry.EpisodeNumber)).ToList();
            var (slots, reason) = PickSlots(numbered.Select(item => item.Candidate).ToList(), open);
            if (slots is null || slots.Count != numbered.Count)
            {
                continue;
            }

            var agrees = numbered.All(item =>
                !placed.TryGetValue(item.Index, out var episode) ||
                episode == slots[item.Candidate.LocalIndex - 1].EpisodeNumber);
            if (!agrees)
            {
                continue;
            }

            assignments.AddRange(numbered
                .Where(item => !placed.ContainsKey(item.Index))
                .Select(item => new SpecialKindAssignment(item.Index, slots[item.Candidate.LocalIndex - 1], reason)));
        }

        return assignments;
    }

    private static (IReadOnlyList<TrackedEpisode>? Slots, SpecialMappingProposalReason Reason) PickSlots(
        IReadOnlyList<SpecialMappingCandidate> files,
        IReadOnlyList<TrackedEpisode> open)
    {
        var ovaFiles = files.All(file =>
            file.NamingPattern == SpecialMappingNamingPattern.OvaDash ||
            OvaTitleRegex.IsMatch(Path.GetFileNameWithoutExtension(file.FileName)));
        var ovaEntries = open
            .Where(entry => OvaTitleRegex.IsMatch(entry.Title ?? string.Empty) && !AsideTitleRegex.IsMatch(entry.Title ?? string.Empty))
            .ToList();
        if (ovaFiles && ovaEntries.Count > 0)
        {
            // The window names its OVAs: only those count, never a recap or drama CD between them.
            return (ovaEntries, SpecialMappingProposalReason.OvaOrder);
        }

        var main = open.Where(entry => !AsideTitleRegex.IsMatch(entry.Title ?? string.Empty)).ToList();
        return main.Count < open.Count
            ? (main, SpecialMappingProposalReason.WindowWithoutAsides)
            : (null, SpecialMappingProposalReason.None);
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
