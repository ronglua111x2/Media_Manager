using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed record SpecialMappingViolation(string RelativePath, int EpisodeNumber, string Message);

public static class SpecialMappingValidator
{
    /// <summary>
    /// Hard checks on a special mapping: each file and each TMDB S00 episode is used at most once, and every
    /// mapped episode exists. Returns the problems instead of throwing, so one bad pair cannot stop a pack link.
    /// </summary>
    public static IReadOnlyList<SpecialMappingViolation> Validate(
        SpecialMappingResult result,
        IReadOnlyList<TrackedEpisode> specialsEpisodes)
    {
        var validEpisodes = specialsEpisodes
            .Where(episode => episode.SeasonNumber == AppConstants.SpecialsSeasonNumber)
            .Select(episode => episode.EpisodeNumber)
            .ToHashSet();

        var violations = new List<SpecialMappingViolation>();
        var mapped = result.Items.Where(item => item.Episode is not null).ToList();

        foreach (var group in mapped.GroupBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            var episodes = string.Join(", ", group.Select(item => $"S00E{item.Episode!.EpisodeNumber:00}"));
            violations.AddRange(group.Select(item => new SpecialMappingViolation(
                item.RelativePath,
                item.Episode!.EpisodeNumber,
                $"'{FileName(item.RelativePath)}' was mapped more than once ({episodes}); left unmapped.")));
        }

        foreach (var item in mapped.Where(item => !validEpisodes.Contains(item.Episode!.EpisodeNumber)))
        {
            violations.Add(new SpecialMappingViolation(
                item.RelativePath,
                item.Episode!.EpisodeNumber,
                $"'{FileName(item.RelativePath)}' was mapped to S00E{item.Episode.EpisodeNumber:00}, which is not a tracked special; left unmapped."));
        }

        foreach (var group in mapped.GroupBy(item => item.Episode!.EpisodeNumber).Where(group => group.Count() > 1))
        {
            var files = string.Join(", ", group.Select(item => $"'{FileName(item.RelativePath)}'"));
            violations.AddRange(group.Select(item => new SpecialMappingViolation(
                item.RelativePath,
                group.Key,
                $"S00E{group.Key:00} was given to {group.Count()} files ({files}); all left unmapped.")));
        }

        return violations;
    }

    /// <summary>
    /// Drops every pair that breaks a hard check (the file becomes unmapped), adds one warning per problem and
    /// marks the result for review. Returns the input unchanged when there is nothing to drop.
    /// </summary>
    /// <param name="request">When given, Gemini's pairs also go through <see cref="ValidateGeminiPairs"/>.</param>
    public static SpecialMappingResult RemoveViolations(
        SpecialMappingResult result,
        IReadOnlyList<TrackedEpisode> specialsEpisodes,
        SpecialMappingAIRequest? request = null)
    {
        var violations = request is null
            ? Validate(result, specialsEpisodes)
            : Validate(result, specialsEpisodes).Concat(ValidateGeminiPairs(result, request)).ToList();
        if (violations.Count == 0)
        {
            return result;
        }

        var dropped = violations
            .Select(violation => (violation.RelativePath, violation.EpisodeNumber))
            .ToHashSet();
        var messagesByPath = violations
            .GroupBy(violation => violation.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Message, StringComparer.OrdinalIgnoreCase);

        var items = new List<SpecialMappingItem>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in result.Items)
        {
            var isDropped = item.Episode is not null && dropped.Contains((item.RelativePath, item.Episode.EpisodeNumber));
            if (!isDropped)
            {
                if (seenPaths.Add(item.RelativePath))
                {
                    items.Add(item);
                }

                continue;
            }

            if (seenPaths.Add(item.RelativePath))
            {
                items.Add(new SpecialMappingItem
                {
                    RelativePath = item.RelativePath,
                    Episode = null,
                    Source = item.Source,
                    ProposalReason = item.ProposalReason,
                    Reason = messagesByPath[item.RelativePath]
                });
            }
        }

        var warnings = violations.Select(violation => violation.Message).Distinct(StringComparer.Ordinal).ToList();
        return new SpecialMappingResult
        {
            Items = items,
            Warnings = result.Warnings.Concat(warnings).Distinct(StringComparer.Ordinal).ToList(),
            UsedGemini = result.UsedGemini,
            ByPath = items.ToDictionary(item => item.RelativePath, item => item, StringComparer.OrdinalIgnoreCase),
            RequiresReview = true,
            FallbackReason = result.FallbackReason,
            GeminiModel = result.GeminiModel,
            CacheKey = result.CacheKey,
            FromCache = result.FromCache
        };
    }

    /// <summary>
    /// Extra hard checks on Gemini's pairs (PR-14), using what the rules know about each file:
    /// a file whose name says <c>S00Exx</c> keeps that number; the episode aired in the window of the file's parent
    /// season (one entry of slack at each edge); and inside one season the numbered files keep their order.
    /// </summary>
    public static IReadOnlyList<SpecialMappingViolation> ValidateGeminiPairs(
        SpecialMappingResult result,
        SpecialMappingAIRequest request)
    {
        var candidatesByPath = request.Context.Candidates
            .GroupBy(candidate => candidate.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var pairs = result.Items
            .Where(item => item.Source == SpecialMappingSource.Gemini && item.Episode is not null)
            .Select(item => (Item: item, Candidate: candidatesByPath.GetValueOrDefault(item.RelativePath)))
            .Where(pair => pair.Candidate is not null)
            .ToList();
        var violations = new List<SpecialMappingViolation>();

        foreach (var (item, candidate) in pairs)
        {
            var episode = item.Episode!.EpisodeNumber;
            if (candidate!.NamingPattern == SpecialMappingNamingPattern.StandardS00E &&
                candidate.LocalIndex > 0 &&
                candidate.LocalIndex != episode)
            {
                violations.Add(new SpecialMappingViolation(
                    item.RelativePath,
                    episode,
                    $"'{FileName(item.RelativePath)}' says S00E{candidate.LocalIndex:00} in its name, but Gemini gave S00E{episode:00}; left unmapped."));
                continue;
            }

            var windowProblem = CheckSeasonWindow(candidate.ParentSeason, item.Episode, request.SeasonBlocks);
            if (windowProblem is not null)
            {
                violations.Add(new SpecialMappingViolation(
                    item.RelativePath,
                    episode,
                    $"'{FileName(item.RelativePath)}' → S00E{episode:00}: {windowProblem}; left unmapped."));
            }
        }

        foreach (var season in pairs
                     .Where(pair => pair.Candidate!.ParentSeason is > 0 && pair.Candidate.LocalIndex > 0)
                     .GroupBy(pair => pair.Candidate!.ParentSeason!.Value))
        {
            var ordered = season.OrderBy(pair => pair.Candidate!.LocalIndex).ToList();
            var keep = LongestIncreasingRun(ordered.Select(pair => pair.Item.Episode!.EpisodeNumber).ToList());
            for (var position = 0; position < ordered.Count; position++)
            {
                if (keep.Contains(position))
                {
                    continue;
                }

                var item = ordered[position].Item;
                violations.Add(new SpecialMappingViolation(
                    item.RelativePath,
                    item.Episode!.EpisodeNumber,
                    $"'{FileName(item.RelativePath)}' → S00E{item.Episode.EpisodeNumber:00} breaks the order of season {season.Key}'s specials; left unmapped."));
            }
        }

        return violations;
    }

    private static string? CheckSeasonWindow(int? parentSeason, TrackedEpisode episode, IReadOnlyList<SpecialSeasonBlock> blocks)
    {
        if (parentSeason is not > 0 || blocks.Count == 0)
        {
            return null;
        }

        var ordered = blocks.OrderBy(block => block.SeasonNumber).ToList();
        var own = ordered.FindIndex(block => block.SeasonNumber == parentSeason.Value);
        var owner = ordered.FindIndex(block => block.Entries.Any(entry => entry.EpisodeNumber == episode.EpisodeNumber));
        if (own < 0 || owner < 0 || owner == own)
        {
            return null;
        }

        // One entry of slack at each edge: release groups sometimes put the special that aired just before a
        // season's premiere (or right after its finale) with that season.
        if (owner == own - 1 && ordered[owner].Entries[^1].EpisodeNumber == episode.EpisodeNumber)
        {
            return null;
        }

        if (owner == own + 1 && ordered[owner].Entries[0].EpisodeNumber == episode.EpisodeNumber)
        {
            return null;
        }

        return $"it aired in season {ordered[owner].SeasonNumber}'s window, not season {parentSeason.Value}'s";
    }

    /// <summary>Positions of one longest strictly increasing subsequence of <paramref name="values"/>.</summary>
    private static HashSet<int> LongestIncreasingRun(IReadOnlyList<int> values)
    {
        if (values.Count == 0)
        {
            return [];
        }

        var length = new int[values.Count];
        var previous = new int[values.Count];
        var best = 0;
        for (var i = 0; i < values.Count; i++)
        {
            length[i] = 1;
            previous[i] = -1;
            for (var j = 0; j < i; j++)
            {
                if (values[j] < values[i] && length[j] + 1 > length[i])
                {
                    length[i] = length[j] + 1;
                    previous[i] = j;
                }
            }

            if (length[i] > length[best])
            {
                best = i;
            }
        }

        var keep = new HashSet<int>();
        for (var at = best; at >= 0; at = previous[at])
        {
            keep.Add(at);
        }

        return keep;
    }

    private static string FileName(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        return slash >= 0 ? normalized[(slash + 1)..] : normalized;
    }
}
