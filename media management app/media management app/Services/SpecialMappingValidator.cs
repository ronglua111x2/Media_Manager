using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public static class SpecialMappingValidator
{
    public static void Validate(SpecialMappingResult result, IReadOnlyList<TrackedEpisode> specialsEpisodes)
    {
        var validEpisodes = specialsEpisodes
            .Where(episode => episode.SeasonNumber == AppConstants.SpecialsSeasonNumber)
            .Select(episode => episode.EpisodeNumber)
            .ToHashSet();

        var mappedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mappedEpisodes = new HashSet<int>();

        foreach (var item in result.Items.Where(item => item.Episode is not null))
        {
            if (!mappedPaths.Add(item.RelativePath))
            {
                throw new InvalidOperationException($"Duplicate special mapping for path '{item.RelativePath}'.");
            }

            var episodeNumber = item.Episode!.EpisodeNumber;
            if (!validEpisodes.Contains(episodeNumber))
            {
                throw new InvalidOperationException($"Mapped S00E{episodeNumber:00} does not exist in tracked specials.");
            }

            if (!mappedEpisodes.Add(episodeNumber))
            {
                throw new InvalidOperationException($"Duplicate special mapping for S00E{episodeNumber:00}.");
            }
        }
    }
}
