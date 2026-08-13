using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public static class PackTorrentInventoryAnalyzer
{
    public static PackTorrentInventory Analyze(
        IReadOnlyList<(string RelativePath, string FileName)> files,
        IReadOnlyList<TrackedEpisode> episodes,
        IReadOnlyList<TrackedSeason>? seasons = null,
        IReadOnlySet<int>? coveredSeasons = null,
        PackAnalyzeMode mode = PackAnalyzeMode.Inspect,
        SpecialMappingResult? resolvedSpecialMappings = null,
        IAppLogger? logger = null)
    {
        var inventory = new PackTorrentInventory();
        var episodesByKey = episodes.ToDictionary(episode => (episode.SeasonNumber, episode.EpisodeNumber));
        var episodesBySeason = episodes
            .Where(episode => episode.SeasonNumber > 0)
            .GroupBy(episode => episode.SeasonNumber)
            .ToDictionary(group => group.Key, group => group.Count());
        var seasonCountsFromDb = (seasons ?? [])
            .Where(season => season.SeasonNumber > 0)
            .ToDictionary(
                season => season.SeasonNumber,
                season => season.EpisodeCount > 0
                    ? season.EpisodeCount
                    : episodesBySeason.GetValueOrDefault(season.SeasonNumber));

        var coveredSeasonSet = BuildCoveredSeasonSet(mode, episodes, coveredSeasons);
        var specialsEpisodes = episodes
            .Where(episode => episode.SeasonNumber == AppConstants.SpecialsSeasonNumber)
            .OrderBy(episode => episode.EpisodeNumber)
            .ToList();

        var tree = PackFolderTreeAnalyzer.Analyze(files.Select(file => file.RelativePath).ToList());
        var seasonGroups = PackSeasonFileGrouper.Group(files, tree);
        var validEpisodesBySeason = episodes
            .Where(episode => episode.SeasonNumber > 0)
            .GroupBy(episode => episode.SeasonNumber)
            .ToDictionary(
                group => group.Key,
                group => group.Select(episode => episode.EpisodeNumber).ToHashSet() as IReadOnlySet<int>);
        var fallbackPatterns = BuildFallbackPatternsBySeason(seasonGroups, seasonCountsFromDb);
        var resolutionsByPath = ResolveRegularEpisodesBySeason(seasonGroups, validEpisodesBySeason, fallbackPatterns);
        var specialMappings = BuildSpecialMappings(files, tree, specialsEpisodes, resolvedSpecialMappings);

        foreach (var (relativePath, fileName) in files)
        {
            var parsed = TorrentCandidateParser.Parse(fileName, relativePath);
            var pathSeasonHint = tree.PathSeasonHints.TryGetValue(relativePath, out var hint)
                ? hint
                : TorrentCandidateParser.TryGetSeasonHintFromPath(relativePath);
            var entry = ClassifyFile(
                relativePath,
                fileName,
                parsed,
                pathSeasonHint,
                seasonGroups,
                resolutionsByPath,
                episodesByKey,
                specialMappings,
                coveredSeasonSet,
                mode);
            inventory.Files.Add(entry);
        }

        var folderSeasons = tree.FolderCoveredSeasons;
        var groupedSeasons = seasonGroups
            .Where(group => group.SeasonNumber > 0)
            .Select(group => group.SeasonNumber);
        inventory.CoveredSeasons = folderSeasons
            .Concat(groupedSeasons)
            .Distinct()
            .Order()
            .ToList();

        foreach (var group in seasonGroups.Where(item => item.SeasonNumber > 0))
        {
            inventory.EpisodeCountsBySeason[group.SeasonNumber] = group.Files.Count;
        }

        if (mode == PackAnalyzeMode.Inspect && inventory.CoveredSeasons.Count > 1)
        {
            inventory.Warnings.Add($"Multi-season pack covers {string.Join(", ", inventory.CoveredSeasons.Select(season => $"S{season:00}"))}.");
        }

        if (inventory.MovieCount > 0)
        {
            inventory.Warnings.Add("Movies detected — add separately in Movies library.");
        }

        if (logger is not null)
        {
            LogInspectDiagnostics(logger, files, tree, seasonGroups, inventory, mode);
        }

        return inventory;
    }

    private static void LogInspectDiagnostics(
        IAppLogger logger,
        IReadOnlyList<(string RelativePath, string FileName)> files,
        PackFolderTreeAnalysis tree,
        IReadOnlyList<PackSeasonFileGrouper.SeasonFileGroup> seasonGroups,
        PackTorrentInventory inventory,
        PackAnalyzeMode mode)
    {
        var folderSeasons = tree.FolderCoveredSeasons.Count == 0
            ? "-"
            : string.Join(",", tree.FolderCoveredSeasons.Select(season => $"S{season:00}"));
        logger.Info(
            $"Pack inspect tree: folderSeasons=[{folderSeasons}] extrasFolder={tree.HasExtrasFolder} specialsFolder={tree.HasSpecialsFolder} moviesFolder={tree.HasMoviesFolder} mode={mode}",
            LogTarget.File);

        foreach (var group in seasonGroups)
        {
            var label = group.SeasonNumber > 0 ? $"S{group.SeasonNumber:00}" : "flat/0";
            logger.Info(
                $"Pack inspect group {label}: {group.Files.Count} file(s)",
                LogTarget.File);
        }

        foreach (var (relativePath, fileName) in files)
        {
            var parsed = TorrentCandidateParser.Parse(fileName, relativePath);
            var pathSeasonHint = tree.PathSeasonHints.TryGetValue(relativePath, out var hint)
                ? hint
                : TorrentCandidateParser.TryGetSeasonHintFromPath(relativePath);
            var owningGroup = seasonGroups.FirstOrDefault(group =>
                group.Files.Any(file => string.Equals(file.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase)));
            var groupLabel = owningGroup is null
                ? "ungrouped"
                : owningGroup.SeasonNumber > 0 ? $"S{owningGroup.SeasonNumber:00}" : "flat/0";
            var entry = inventory.Files.FirstOrDefault(file =>
                string.Equals(file.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
            var excludeReason = PackSeasonFileGrouper.GetEpisodeGroupExcludeReason(relativePath, fileName) ?? "-";
            var coveredFromName = parsed.CoveredSeasons.Count == 0
                ? "-"
                : string.Join(",", parsed.CoveredSeasons.Select(season => $"S{season:00}"));

            logger.Info(
                $"Pack inspect file '{fileName}' path='{relativePath}' parsedSeason={FormatSeason(parsed.SeasonNumber)} parsedEpisode={FormatEpisode(parsed.EpisodeNumber)} coveredFromName=[{coveredFromName}] pathHint={FormatSeason(pathSeasonHint)} extra={parsed.IsExtraContent} special={parsed.IsSpecialContent} exclude={excludeReason} group={groupLabel} class={entry?.Classification.ToString() ?? "-"} matched={FormatSeason(entry?.MatchedSeasonNumber)}E{FormatEpisode(entry?.MatchedEpisodeNumber)} reason='{entry?.MatchReason}'",
                LogTarget.File);
        }

        var covered = inventory.CoveredSeasons.Count == 0
            ? "-"
            : string.Join(",", inventory.CoveredSeasons.Select(season => $"S{season:00}"));
        logger.Info(
            $"Pack inspect result: coveredSeasons=[{covered}] regular={inventory.RegularEpisodeCount} specials={inventory.MatchedSpecialCount} extras={inventory.UnmatchedExtraCount} skipped={inventory.SkippedCount} movies={inventory.MovieCount}",
            LogTarget.File);
    }

    private static string FormatSeason(int? season) =>
        season is null ? "-" : $"S{season.Value:00}";

    private static string FormatEpisode(int? episode) =>
        episode is null ? "-" : episode.Value.ToString("00");

    private static HashSet<int> BuildCoveredSeasonSet(
        PackAnalyzeMode mode,
        IReadOnlyList<TrackedEpisode> episodes,
        IReadOnlySet<int>? coveredSeasons)
    {
        if (mode == PackAnalyzeMode.Link)
        {
            var set = coveredSeasons is { Count: > 0 }
                ? coveredSeasons.ToHashSet()
                : new HashSet<int> { 1 };

            if (episodes.Any(episode => episode.SeasonNumber == AppConstants.SpecialsSeasonNumber))
            {
                set.Add(AppConstants.SpecialsSeasonNumber);
            }

            return set;
        }

        return episodes
            .Where(episode => episode.SeasonNumber > 0)
            .Select(episode => episode.SeasonNumber)
            .ToHashSet();
    }

    private static Dictionary<string, PackEpisodeResolution> ResolveRegularEpisodesBySeason(
        IReadOnlyList<PackSeasonFileGrouper.SeasonFileGroup> seasonGroups,
        IReadOnlyDictionary<int, IReadOnlySet<int>> validEpisodesBySeason,
        IReadOnlyDictionary<int, InferredEpisodePattern> fallbackPatterns)
    {
        var resolutions = new Dictionary<string, PackEpisodeResolution>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in seasonGroups.Where(item => item.SeasonNumber > 0))
        {
            var validEpisodes = validEpisodesBySeason.GetValueOrDefault(group.SeasonNumber) ?? new HashSet<int>();
            fallbackPatterns.TryGetValue(group.SeasonNumber, out var fallbackPattern);
            var seasonResolutions = PackEpisodeResolver.ResolveSeason(
                group.Files,
                group.SeasonNumber,
                validEpisodes,
                fallbackPattern?.IsValid == true ? fallbackPattern : null);

            foreach (var (path, resolution) in seasonResolutions)
            {
                resolutions[path] = resolution;
            }
        }

        return resolutions;
    }

    private static Dictionary<int, InferredEpisodePattern> BuildFallbackPatternsBySeason(
        IReadOnlyList<PackSeasonFileGrouper.SeasonFileGroup> seasonGroups,
        IReadOnlyDictionary<int, int> seasonCountsFromDb)
    {
        var patterns = new Dictionary<int, InferredEpisodePattern>();
        var borrowCandidates = new List<InferredEpisodePattern>();

        foreach (var group in seasonGroups.Where(item => item.SeasonNumber > 0).OrderByDescending(item => item.Files.Count))
        {
            var stems = group.Files
                .Select(file => PackEpisodePatternInferrer.NormalizeStem(Path.GetFileNameWithoutExtension(file.FileName)))
                .Where(stem => !string.IsNullOrWhiteSpace(stem))
                .ToList();
            var expectedCount = seasonCountsFromDb.GetValueOrDefault(group.SeasonNumber);
            var pattern = PackEpisodePatternInferrer.Infer(
                stems,
                expectedCount > 0 ? expectedCount : null,
                group.SeasonNumber,
                borrowCandidates);

            patterns[group.SeasonNumber] = pattern;
            if (pattern.IsValid)
            {
                borrowCandidates.Add(pattern);
            }
        }

        return patterns;
    }

    private static IReadOnlyDictionary<string, SpecialFileMapping> BuildSpecialMappings(
        IReadOnlyList<(string RelativePath, string FileName)> files,
        PackFolderTreeAnalysis tree,
        IReadOnlyList<TrackedEpisode> specialsEpisodes,
        SpecialMappingResult? resolvedSpecialMappings)
    {
        if (resolvedSpecialMappings is not null)
        {
            return BuildSpecialMappingsFromResult(resolvedSpecialMappings);
        }

        return BuildLegacySpecialMappings(files, tree, specialsEpisodes);
    }

    private static IReadOnlyDictionary<string, SpecialFileMapping> BuildSpecialMappingsFromResult(
        SpecialMappingResult resolvedSpecialMappings)
    {
        var mappings = new Dictionary<string, SpecialFileMapping>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in resolvedSpecialMappings.Items)
        {
            mappings[item.RelativePath] = new SpecialFileMapping(
                item.Episode,
                item.Reason,
                item.Source,
                item.ProposalReason);
        }

        return mappings;
    }

    private static IReadOnlyDictionary<string, SpecialFileMapping> BuildLegacySpecialMappings(
        IReadOnlyList<(string RelativePath, string FileName)> files,
        PackFolderTreeAnalysis tree,
        IReadOnlyList<TrackedEpisode> specialsEpisodes)
    {
        var mappings = new Dictionary<string, SpecialFileMapping>(StringComparer.OrdinalIgnoreCase);
        var claimed = new HashSet<int>();
        var buckets = PackSpecialBucketDetector.Detect(files, tree);

        foreach (var bucket in buckets.OrderBy(item => item.LayoutKind).ThenBy(item => item.ParentSeasonNumber))
        {
            var stems = bucket.Files
                .Select(file => Path.GetFileNameWithoutExtension(file.FileName))
                .Where(stem => !string.IsNullOrWhiteSpace(stem))
                .ToList();
            var pattern = PackSpecialPatternInferrer.Infer(stems, specialsEpisodes.Count);
            var bucketMap = PackSpecialEpisodeMapper.MapBucket(bucket, pattern, specialsEpisodes, claimed);

            foreach (var file in bucket.Files)
            {
                if (!bucketMap.TryGetValue(file.RelativePath, out var episode) || episode is null)
                {
                    mappings[file.RelativePath] = new SpecialFileMapping(
                        null,
                        $"Special/OVA ({bucket.LayoutKind}) without TMDB match.");
                    continue;
                }

                claimed.Add(episode.EpisodeNumber);
                mappings[file.RelativePath] = new SpecialFileMapping(
                    episode,
                    $"Matched TMDB S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00} via {bucket.LayoutKind}.");
            }
        }

        return mappings;
    }

    private sealed record SpecialFileMapping(
        TrackedEpisode? Episode,
        string Reason,
        SpecialMappingSource? Source = null,
        SpecialMappingProposalReason? ProposalReason = null);

    private static PackFileEntry ClassifyFile(
        string relativePath,
        string fileName,
        TorrentCandidateParseResult parsed,
        int? pathSeasonHint,
        IReadOnlyList<PackSeasonFileGrouper.SeasonFileGroup> seasonGroups,
        IReadOnlyDictionary<string, PackEpisodeResolution> resolutionsByPath,
        IReadOnlyDictionary<(int SeasonNumber, int EpisodeNumber), TrackedEpisode> episodesByKey,
        IReadOnlyDictionary<string, SpecialFileMapping> specialMappings,
        IReadOnlySet<int> coveredSeasons,
        PackAnalyzeMode mode)
    {
        if (IsMoviePackFile(relativePath, fileName) || IsUnderMoviesFolder(relativePath))
        {
            return new PackFileEntry
            {
                RelativePath = relativePath,
                FileName = fileName,
                Classification = PackFileClassification.Movie,
                MatchReason = "Movie pack file."
            };
        }

        if (IsUnderExtrasFolder(relativePath) || parsed.IsExtraContent)
        {
            return new PackFileEntry
            {
                RelativePath = relativePath,
                FileName = fileName,
                Classification = PackFileClassification.UnmatchedExtra,
                MatchReason = IsUnderExtrasFolder(relativePath)
                    ? "Extra content (Extras folder)."
                    : "Extra content (NCED/NCOP or Extras folder)."
            };
        }

        if (specialMappings.TryGetValue(relativePath, out var specialMapping))
        {
            if (specialMapping.Episode is not null)
            {
                return new PackFileEntry
                {
                    RelativePath = relativePath,
                    FileName = fileName,
                    Classification = PackFileClassification.MatchedSpecial,
                    MatchedSeasonNumber = specialMapping.Episode.SeasonNumber,
                    MatchedEpisodeNumber = specialMapping.Episode.EpisodeNumber,
                    MatchReason = specialMapping.Reason,
                    MappingSource = specialMapping.Source,
                    ProposalReason = specialMapping.ProposalReason
                };
            }

            return new PackFileEntry
            {
                RelativePath = relativePath,
                FileName = fileName,
                Classification = PackFileClassification.UnmatchedExtra,
                MatchReason = specialMapping.Reason
            };
        }

        var seasonNumber = ResolveSeason(relativePath, parsed, pathSeasonHint, seasonGroups);
        if (seasonNumber is null or <= 0)
        {
            return Skipped(relativePath, fileName, "No season folder or episode pattern.");
        }

        if (PackSeasonFileGrouper.IsExcludedFromEpisodeGroup(relativePath, fileName))
        {
            return Skipped(relativePath, fileName, "Excluded from episode group.");
        }

        if (mode == PackAnalyzeMode.Link && !coveredSeasons.Contains(seasonNumber.Value))
        {
            return Skipped(relativePath, fileName, "Season is outside pack coverage.");
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        int? episodeNumber = null;
        string? resolutionReason = null;

        if (resolutionsByPath.TryGetValue(relativePath, out var resolution))
        {
            if (resolution.Status == PackEpisodeResolutionStatus.Skipped)
            {
                if (mode == PackAnalyzeMode.Link)
                {
                    return Skipped(relativePath, fileName, resolution.SkipReason ?? "No tracked episode match.");
                }

                resolutionReason = resolution.SkipReason;
            }
            else
            {
                episodeNumber = resolution.EpisodeNumber;
            }
        }

        if (episodeNumber is null && resolutionReason is null)
        {
            episodeNumber = PackEpisodePatternInferrer.TryInferEpisode(
                stem,
                seasonNumber.Value,
                episodesByKey.Values
                    .Where(episode => episode.SeasonNumber == seasonNumber.Value)
                    .Select(episode => episode.EpisodeNumber)
                    .ToHashSet());
        }

        if (mode == PackAnalyzeMode.Link)
        {
            if (episodeNumber is null ||
                !episodesByKey.ContainsKey((seasonNumber.Value, episodeNumber.Value)))
            {
                return Skipped(relativePath, fileName, "No tracked episode match.");
            }

            return new PackFileEntry
            {
                RelativePath = relativePath,
                FileName = fileName,
                Classification = PackFileClassification.RegularEpisode,
                MatchedSeasonNumber = seasonNumber,
                MatchedEpisodeNumber = episodeNumber,
                MatchReason = $"Resolved S{seasonNumber:00}E{episodeNumber:00}, matched TMDB."
            };
        }

        if (episodeNumber is null)
        {
            if (resolutionReason is not null)
            {
                return new PackFileEntry
                {
                    RelativePath = relativePath,
                    FileName = fileName,
                    Classification = PackFileClassification.Skipped,
                    MatchedSeasonNumber = seasonNumber,
                    MatchReason = resolutionReason
                };
            }

            return new PackFileEntry
            {
                RelativePath = relativePath,
                FileName = fileName,
                Classification = PackFileClassification.RegularEpisode,
                MatchedSeasonNumber = seasonNumber,
                MatchReason = $"Season folder S{seasonNumber:00} video file."
            };
        }

        var hasDbMatch = episodesByKey.ContainsKey((seasonNumber.Value, episodeNumber.Value));
        return new PackFileEntry
        {
            RelativePath = relativePath,
            FileName = fileName,
            Classification = PackFileClassification.RegularEpisode,
            MatchedSeasonNumber = seasonNumber,
            MatchedEpisodeNumber = episodeNumber,
            MatchReason = hasDbMatch
                ? $"Resolved S{seasonNumber:00}E{episodeNumber:00}, matched TMDB."
                : $"Resolved S{seasonNumber:00}E{episodeNumber:00}."
        };
    }

    private static PackFileEntry Skipped(string relativePath, string fileName, string reason) =>
        new()
        {
            RelativePath = relativePath,
            FileName = fileName,
            Classification = PackFileClassification.Skipped,
            MatchReason = reason
        };

    private static int? ResolveSeason(
        string relativePath,
        TorrentCandidateParseResult parsed,
        int? pathSeasonHint,
        IReadOnlyList<PackSeasonFileGrouper.SeasonFileGroup> seasonGroups)
    {
        if (pathSeasonHint is > 0)
        {
            return pathSeasonHint;
        }

        var owningGroup = seasonGroups.FirstOrDefault(group =>
            group.Files.Any(file => string.Equals(file.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase)));
        if (owningGroup?.SeasonNumber > 0)
        {
            return owningGroup.SeasonNumber;
        }

        return parsed.SeasonNumber;
    }

    private static bool IsMoviePackFile(string relativePath, string fileName)
    {
        var normalizedPath = relativePath.Replace('\\', '/');
        return normalizedPath
                   .Split('/', StringSplitOptions.RemoveEmptyEntries)
                   .Any(segment => string.Equals(segment, "movies", StringComparison.OrdinalIgnoreCase)) ||
               fileName.Contains("movie", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnderMoviesFolder(string relativePath) =>
        PathContainsFolder(relativePath, "movies") || PathContainsFolder(relativePath, "films");

    private static bool IsUnderExtrasFolder(string relativePath) =>
        PathContainsFolder(relativePath, "extras") || PathContainsFolder(relativePath, "extra");

    private static bool PathContainsFolder(string relativePath, string folderName)
    {
        return relativePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => string.Equals(segment, folderName, StringComparison.OrdinalIgnoreCase));
    }
}
