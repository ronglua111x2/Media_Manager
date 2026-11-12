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
    public static SpecialMappingResult RemoveViolations(
        SpecialMappingResult result,
        IReadOnlyList<TrackedEpisode> specialsEpisodes)
    {
        var violations = Validate(result, specialsEpisodes);
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
            FallbackReason = result.FallbackReason
        };
    }

    private static string FileName(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        return slash >= 0 ? normalized[(slash + 1)..] : normalized;
    }
}
