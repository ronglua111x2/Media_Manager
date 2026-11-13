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
        IAppLogger? logger = null,
        int? ownerSeasonNumber = null,
        IReadOnlyDictionary<string, long>? fileSizes = null)
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
        var seasonGroups = PromoteFlatSeasonGroups(
            PackSeasonFileGrouper.Group(files, tree),
            ownerSeasonNumber,
            out var flatPromotion);
        var validEpisodesBySeason = episodes
            .Where(episode => episode.SeasonNumber > 0)
            .GroupBy(episode => episode.SeasonNumber)
            .ToDictionary(
                group => group.Key,
                group => group.Select(episode => episode.EpisodeNumber).ToHashSet() as IReadOnlySet<int>);
        var fallbackPatterns = BuildFallbackPatternsBySeason(seasonGroups, seasonCountsFromDb);
        var absoluteMapper = new AbsoluteEpisodeMapper(BuildSeasonEpisodeCounts(seasonCountsFromDb, episodesBySeason));
        var episodesByAirDate = episodes
            .Where(episode => episode.SeasonNumber > 0 && episode.AirDate is not null)
            .GroupBy(episode => DateOnly.FromDateTime(episode.AirDate!.Value))
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<(int Season, int Episode)>)group.Select(episode => (episode.SeasonNumber, episode.EpisodeNumber)).ToList());
        var resolutionsByPath = ResolveRegularEpisodesBySeason(
            seasonGroups,
            validEpisodesBySeason,
            fallbackPatterns,
            fileSizes,
            absoluteMapper,
            episodesByAirDate,
            tree,
            inventory.Warnings);
        FlagSpecialLikeEpisodes(resolutionsByPath, files, fileSizes, episodes, inventory.Warnings);
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
        // Seasons the files resolve to count too: absolute numbering can place a flat batch's "- 13".."- 24" in
        // season 2. Inspect stores this as the pack's coverage and Link skips anything outside it (audit R2-B-02).
        var resolvedSeasons = inventory.Files
            .Where(file => file is { Classification: PackFileClassification.RegularEpisode, MatchedSeasonNumber: > 0 })
            .Select(file => file.MatchedSeasonNumber!.Value);
        inventory.CoveredSeasons = folderSeasons
            .Concat(groupedSeasons)
            .Concat(resolvedSeasons)
            .Distinct()
            .Order()
            .ToList();

        // Count episodes the files resolve to, not files in a season folder (PARSE-15 note: Inspect
        // used to count unresolved files as episodes).
        foreach (var group in inventory.Files
                     .Where(file => file is { Classification: PackFileClassification.RegularEpisode, MatchedSeasonNumber: > 0, MatchedEpisodeNumber: not null })
                     .GroupBy(file => file.MatchedSeasonNumber!.Value))
        {
            inventory.EpisodeCountsBySeason[group.Key] = group
                .SelectMany(file => Enumerable.Range(
                    file.MatchedEpisodeNumber!.Value,
                    (file.MatchedLastEpisodeNumber ?? file.MatchedEpisodeNumber!.Value) - file.MatchedEpisodeNumber!.Value + 1))
                .Distinct()
                .Count();
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
            LogInspectDiagnostics(logger, files, tree, seasonGroups, inventory, mode, flatPromotion);
        }

        return inventory;
    }

    private static void LogInspectDiagnostics(
        IAppLogger logger,
        IReadOnlyList<(string RelativePath, string FileName)> files,
        PackFolderTreeAnalysis tree,
        IReadOnlyList<PackSeasonFileGrouper.SeasonFileGroup> seasonGroups,
        PackTorrentInventory inventory,
        PackAnalyzeMode mode,
        FlatPromotionDiagnostics flatPromotion)
    {
        var folderSeasons = tree.FolderCoveredSeasons.Count == 0
            ? "-"
            : string.Join(",", tree.FolderCoveredSeasons.Select(season => $"S{season:00}"));
        logger.Info(
            $"Pack inspect tree: folderSeasons=[{folderSeasons}] extrasFolder={tree.HasExtrasFolder} specialsFolder={tree.HasSpecialsFolder} moviesFolder={tree.HasMoviesFolder} mode={mode}",
            LogTarget.File);

        if (flatPromotion.HadFlatGroup)
        {
            logger.Info(
                $"Pack inspect flat promotion: patternValid={flatPromotion.PatternValid} prefixLen={flatPromotion.PrefixLength} suffixLen={flatPromotion.SuffixLength} prefix='{flatPromotion.PrefixSample}' assignment={flatPromotion.Assignment} assignedSeasons=[{flatPromotion.AssignedSeasonsDisplay}]",
                LogTarget.File);
        }

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

    private sealed class FlatPromotionDiagnostics
    {
        public bool HadFlatGroup { get; init; }

        public bool PatternValid { get; init; }

        public int PrefixLength { get; init; }

        public int SuffixLength { get; init; }

        public string PrefixSample { get; init; } = string.Empty;

        public string Assignment { get; init; } = "-";

        public IReadOnlyList<int> AssignedSeasons { get; init; } = [];

        public string AssignedSeasonsDisplay => AssignedSeasons.Count == 0
            ? "-"
            : string.Join(",", AssignedSeasons.Select(season => $"S{season:00}"));
    }

    private static IReadOnlyList<PackSeasonFileGrouper.SeasonFileGroup> PromoteFlatSeasonGroups(
        IReadOnlyList<PackSeasonFileGrouper.SeasonFileGroup> seasonGroups,
        int? ownerSeasonNumber,
        out FlatPromotionDiagnostics diagnostics)
    {
        var flat = seasonGroups.FirstOrDefault(group => group.SeasonNumber == PackSeasonFileGrouper.FlatSeasonKey);
        if (flat is null || flat.Files.Count == 0)
        {
            diagnostics = new FlatPromotionDiagnostics();
            return seasonGroups;
        }

        var promoted = new Dictionary<int, PackSeasonFileGrouper.SeasonFileGroup>();
        foreach (var group in seasonGroups.Where(item => item.SeasonNumber != PackSeasonFileGrouper.FlatSeasonKey))
        {
            promoted[group.SeasonNumber] = new PackSeasonFileGrouper.SeasonFileGroup
            {
                SeasonNumber = group.SeasonNumber,
                Files = [.. group.Files]
            };
        }

        var stems = flat.Files
            .Select(file => PackEpisodePatternInferrer.NormalizeStem(Path.GetFileNameWithoutExtension(file.FileName)))
            .Where(stem => !string.IsNullOrWhiteSpace(stem))
            .ToList();
        var pattern = PackEpisodePatternInferrer.Infer(stems);
        var assignedSeasons = new SortedSet<int>();
        var assignmentKinds = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        if (pattern.IsValid)
        {
            foreach (var file in flat.Files)
            {
                var stem = PackEpisodePatternInferrer.NormalizeStem(Path.GetFileNameWithoutExtension(file.FileName));
                var prefixSeason = PackEpisodePatternInferrer.TryExtractSeasonFromPrefix(stem, pattern);
                var season = prefixSeason is > 0 ? prefixSeason : ownerSeasonNumber;
                if (season is not > 0)
                {
                    AddToGroup(promoted, PackSeasonFileGrouper.FlatSeasonKey, file);
                    continue;
                }

                AddToGroup(promoted, season.Value, file);
                assignedSeasons.Add(season.Value);
                assignmentKinds.Add(prefixSeason is > 0 ? "prefix-Sxx" : "owner-fallback");
            }
        }
        else if (ownerSeasonNumber is > 0)
        {
            foreach (var file in flat.Files)
            {
                AddToGroup(promoted, ownerSeasonNumber.Value, file);
            }

            assignedSeasons.Add(ownerSeasonNumber.Value);
            assignmentKinds.Add("infer-failed-owner");
        }
        else
        {
            foreach (var file in flat.Files)
            {
                AddToGroup(promoted, PackSeasonFileGrouper.FlatSeasonKey, file);
            }

            assignmentKinds.Add("infer-failed");
        }

        var prefixSample = string.Empty;
        if (pattern.IsValid && pattern.PrefixLength > 0 && stems.Count > 0)
        {
            var sample = stems[0];
            prefixSample = pattern.PrefixLength <= sample.Length
                ? sample[..pattern.PrefixLength]
                : sample;
        }

        diagnostics = new FlatPromotionDiagnostics
        {
            HadFlatGroup = true,
            PatternValid = pattern.IsValid,
            PrefixLength = pattern.PrefixLength,
            SuffixLength = pattern.SuffixLength,
            PrefixSample = prefixSample,
            Assignment = assignmentKinds.Count == 0 ? "-" : string.Join("+", assignmentKinds),
            AssignedSeasons = assignedSeasons.ToList()
        };

        return promoted.Values.OrderBy(group => group.SeasonNumber).ToList();
    }

    private static void AddToGroup(
        Dictionary<int, PackSeasonFileGrouper.SeasonFileGroup> groups,
        int seasonNumber,
        (string RelativePath, string FileName) file)
    {
        if (!groups.TryGetValue(seasonNumber, out var group))
        {
            group = new PackSeasonFileGrouper.SeasonFileGroup { SeasonNumber = seasonNumber };
            groups[seasonNumber] = group;
        }

        group.Files.Add(file);
    }

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
        IReadOnlyDictionary<int, InferredEpisodePattern> fallbackPatterns,
        IReadOnlyDictionary<string, long>? fileSizes,
        AbsoluteEpisodeMapper absoluteMapper,
        IReadOnlyDictionary<DateOnly, IReadOnlyList<(int Season, int Episode)>> episodesByAirDate,
        PackFolderTreeAnalysis tree,
        List<string> warnings)
    {
        var resolutions = new Dictionary<string, PackEpisodeResolution>(StringComparer.OrdinalIgnoreCase);
        var fileNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in seasonGroups.Where(item => item.SeasonNumber > 0))
        {
            var validEpisodes = validEpisodesBySeason.GetValueOrDefault(group.SeasonNumber) ?? new HashSet<int>();
            fallbackPatterns.TryGetValue(group.SeasonNumber, out var fallbackPattern);
            var pattern = fallbackPattern?.IsValid == true ? fallbackPattern : null;

            // "Season N Part 2" folders are resolved on their own so a renumbered part cannot collide
            // with (and knock out) the files of part 1.
            var partFiles = group.Files
                .Select(file => (File: file, Part: TorrentCandidateParser.TryGetSeasonPartFromPath(file.RelativePath)))
                .Where(item => item.Part is not null)
                .ToList();
            var mainFiles = group.Files
                .Where(file => TorrentCandidateParser.TryGetSeasonPartFromPath(file.RelativePath) is null)
                .ToList();

            // Files whose folder names this season: absolute numbers there must not be moved to
            // another season by TMDB episode counts (audit L1-H2).
            var seasonFolderPaths = mainFiles
                .Where(file => (tree.PathSeasonHints.TryGetValue(file.RelativePath, out var hint)
                                   ? hint
                                   : TorrentCandidateParser.TryGetSeasonHintFromPath(file.RelativePath)) == group.SeasonNumber)
                .Select(file => file.RelativePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var seasonResolutions = PackEpisodeResolver.ResolveSeason(
                mainFiles,
                group.SeasonNumber,
                validEpisodes,
                pattern,
                fileSizes,
                absoluteMapper,
                episodesByAirDate,
                seasonFolderPaths);
            foreach (var (path, resolution) in seasonResolutions)
            {
                resolutions[path] = resolution;
            }

            foreach (var partGroup in partFiles.GroupBy(item => item.Part!.Value))
            {
                var partResolutions = PackEpisodeResolver.ResolveSeason(
                    partGroup.Select(item => item.File).ToList(),
                    group.SeasonNumber,
                    validEpisodes,
                    pattern,
                    fileSizes,
                    absoluteMapper,
                    episodesByAirDate);
                var linked = partResolutions.Values
                    .Where(resolution => resolution.Status == PackEpisodeResolutionStatus.RegularEpisode)
                    .Select(resolution => (resolution.SeasonNumber, Episode: resolution.EpisodeNumber!.Value))
                    .ToList();
                var mainLinked = seasonResolutions.Values
                    .Where(resolution => resolution.Status == PackEpisodeResolutionStatus.RegularEpisode)
                    .Select(resolution => (resolution.SeasonNumber, Episode: resolution.EpisodeNumber!.Value))
                    .ToHashSet();
                var (partSeason, part) = partGroup.Key;
                // A restart is part 2 numbered from 1 again inside the folder's own season. Files that absolute
                // numbering placed in the next TMDB season (where part 2 is its own season) are not (audit L1-L6).
                var restarts = linked.Any(mainLinked.Contains) ||
                               linked.Any(item => item.SeasonNumber == partSeason && item.Episode == 1);
                foreach (var (path, resolution) in partResolutions)
                {
                    resolutions[path] = restarts
                        ? PackEpisodeResolution.Skipped(
                            group.SeasonNumber,
                            $"Folder 'Season {partSeason} Part {part}' restarts episode numbers; not linking it, review which TMDB episodes it holds.")
                        : resolution;
                }

                if (restarts)
                {
                    warnings.Add($"Season {partSeason} Part {part} restarts episode numbers; its files were not linked and need review.");
                }
            }

            foreach (var file in group.Files)
            {
                fileNames[file.RelativePath] = file.FileName;
            }
        }

        // Absolute numbers can resolve to another season than the file's group, so two groups can
        // claim one episode; apply the same collision rule across groups.
        var crossGroup = resolutions
            .Where(pair => pair.Value.Status == PackEpisodeResolutionStatus.RegularEpisode && pair.Value.EpisodeNumber is not null)
            .Select(pair => new PackEpisodeCandidate(pair.Key, fileNames[pair.Key], pair.Value.SeasonNumber, pair.Value.EpisodeNumber!.Value, pair.Value.LastEpisodeNumber))
            .GroupBy(candidate => (candidate.Season, candidate.Episode))
            .Where(group => group.Count() > 1)
            .SelectMany(group => group)
            .ToList();
        foreach (var (path, resolution) in PackEpisodeResolver.ResolveCollisions(crossGroup, fileSizes))
        {
            resolutions[path] = resolution;
        }

        return resolutions;
    }

    private static readonly HashSet<string> TitleStopWords = new(
        ["a", "an", "the", "of", "and", "to", "in", "on", "at", "for", "with", "is", "episode", "ep", "special", "specials", "ova", "part"],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Report section 10.4 point 5: a release of shorts or specials named like regular episodes
    /// ("Show - 01..06", no "Special" word, no Specials folder) would be linked as episodes 1-6.
    /// Files without an explicit SxxEyy are held back for review when their title matches a TMDB
    /// special and no regular episode, or when they are far smaller than the pack's other episodes.
    /// </summary>
    private static void FlagSpecialLikeEpisodes(
        Dictionary<string, PackEpisodeResolution> resolutions,
        IReadOnlyList<(string RelativePath, string FileName)> files,
        IReadOnlyDictionary<string, long>? fileSizes,
        IReadOnlyList<TrackedEpisode> episodes,
        List<string> warnings)
    {
        var linked = files
            .Where(file => resolutions.TryGetValue(file.RelativePath, out var resolution) &&
                           resolution.Status == PackEpisodeResolutionStatus.RegularEpisode)
            .ToList();
        if (linked.Count == 0)
        {
            return;
        }

        var specialTitles = episodes
            .Where(episode => episode.SeasonNumber == AppConstants.SpecialsSeasonNumber)
            .Select(episode => (episode.EpisodeNumber, Tokens: TitleTokens(episode.Title)))
            .Where(item => item.Tokens.Count >= 2)
            .ToList();
        var regularTitles = episodes
            .Where(episode => episode.SeasonNumber > 0)
            .Select(episode => TitleTokens(episode.Title))
            .Where(tokens => tokens.Count >= 2)
            .ToList();
        // Median per quality: an SD episode is far smaller than a 1080p one by nature, so a mixed-quality
        // batch must not hold back all its SD episodes (audit L1-L3).
        var medianByQuality = linked
            .Select(file => (Quality: TorrentQuality.Detect(file.FileName), Size: GetFileSize(fileSizes, file.RelativePath)))
            .Where(item => item.Size > 0)
            .GroupBy(item => item.Quality, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() >= 3)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var ordered = group.Select(item => item.Size).Order().ToList();
                    return ordered[ordered.Count / 2];
                },
                StringComparer.OrdinalIgnoreCase);
        var flagged = 0;

        foreach (var (relativePath, fileName) in linked)
        {
            if (TorrentCandidateParser.Parse(fileName, relativePath) is { SeasonNumber: > 0, EpisodeNumber: not null })
            {
                continue;
            }

            var resolution = resolutions[relativePath];
            string? reason = null;
            var titleTokens = TitleTokens(TorrentCandidateParser.Parse(fileName, relativePath, allowAnimeAbsolute: true).EpisodeTitle);
            if (titleTokens.Count >= 2 && !regularTitles.Any(tokens => tokens.IsSubsetOf(titleTokens)))
            {
                var special = specialTitles.FirstOrDefault(item => item.Tokens.IsSubsetOf(titleTokens));
                if (special.Tokens is not null)
                {
                    reason = $"Title matches special S00E{special.EpisodeNumber:00} and no regular episode; may be a special named like an episode, review before linking.";
                }
            }

            var size = GetFileSize(fileSizes, relativePath);
            var medianSize = medianByQuality.GetValueOrDefault(TorrentQuality.Detect(fileName));
            if (reason is null && medianSize > 0 && size > 0 && size * 4 < medianSize)
            {
                reason = $"File is {size / 1_048_576} MB, far smaller than the pack's other episodes ({medianSize / 1_048_576} MB); may be a short or special named like an episode, review before linking.";
            }

            if (reason is null)
            {
                continue;
            }

            resolutions[relativePath] = PackEpisodeResolution.Skipped(resolution.SeasonNumber, reason);
            flagged++;
        }

        if (flagged > 0)
        {
            warnings.Add($"{flagged} file(s) look like specials or shorts named as regular episodes; they were not linked and need review.");
        }
    }

    private static HashSet<string> TitleTokens(string? title) =>
        string.IsNullOrWhiteSpace(title)
            ? []
            : TorrentCandidateParser.Tokenize(title)
                .Where(token => token.Length > 1 && !token.All(char.IsDigit) && !TitleStopWords.Contains(token))
                .Select(token => token.ToLowerInvariant())
                .ToHashSet();

    private static long GetFileSize(IReadOnlyDictionary<string, long>? fileSizes, string relativePath) =>
        fileSizes is not null && fileSizes.TryGetValue(relativePath, out var size) ? size : 0;

    private static Dictionary<int, int> BuildSeasonEpisodeCounts(
        IReadOnlyDictionary<int, int> seasonCountsFromDb,
        IReadOnlyDictionary<int, int> episodesBySeason)
    {
        var counts = new Dictionary<int, int>(episodesBySeason);
        foreach (var (season, count) in seasonCountsFromDb)
        {
            if (season > 0 && count > 0)
            {
                counts[season] = count;
            }
        }

        return counts;
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
        if (TorrentFileRules.IsSampleOrProof(relativePath))
        {
            return Skipped(relativePath, fileName, "Sample or proof file.");
        }

        if (PackMovieRules.IsMovieFile(relativePath, fileName))
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
        resolutionsByPath.TryGetValue(relativePath, out var resolution);
        if (resolution is { Status: PackEpisodeResolutionStatus.RegularEpisode, SeasonNumber: > 0 })
        {
            // Absolute numbering may place the file in another season than its folder or group.
            seasonNumber = resolution.SeasonNumber;
        }

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

        if (resolution is not null)
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

            var lastEpisodeNumber = GetLinkedLastEpisode(resolution, seasonNumber.Value, episodeNumber.Value, episodesByKey);
            return new PackFileEntry
            {
                RelativePath = relativePath,
                FileName = fileName,
                Classification = PackFileClassification.RegularEpisode,
                MatchedSeasonNumber = seasonNumber,
                MatchedEpisodeNumber = episodeNumber,
                MatchedLastEpisodeNumber = lastEpisodeNumber,
                MatchReason = lastEpisodeNumber is null
                    ? $"Resolved S{seasonNumber:00}E{episodeNumber:00}, matched TMDB."
                    : $"Resolved S{seasonNumber:00}E{episodeNumber:00}-E{lastEpisodeNumber:00}, matched TMDB."
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
                Classification = PackFileClassification.Skipped,
                MatchedSeasonNumber = seasonNumber,
                MatchReason = $"No episode number found for S{seasonNumber:00}."
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
            MatchedLastEpisodeNumber = GetLinkedLastEpisode(resolution, seasonNumber.Value, episodeNumber.Value, episodesByKey),
            MatchReason = hasDbMatch
                ? $"Resolved S{seasonNumber:00}E{episodeNumber:00}, matched TMDB."
                : $"Resolved S{seasonNumber:00}E{episodeNumber:00}."
        };
    }

    private static int? GetLinkedLastEpisode(
        PackEpisodeResolution? resolution,
        int seasonNumber,
        int episodeNumber,
        IReadOnlyDictionary<(int SeasonNumber, int EpisodeNumber), TrackedEpisode> episodesByKey)
    {
        if (resolution is not { Status: PackEpisodeResolutionStatus.RegularEpisode, LastEpisodeNumber: { } last } ||
            resolution.SeasonNumber != seasonNumber ||
            resolution.EpisodeNumber != episodeNumber)
        {
            return null;
        }

        for (var episode = episodeNumber + 1; episode <= last; episode++)
        {
            if (!episodesByKey.ContainsKey((seasonNumber, episode)))
            {
                return episode - 1 > episodeNumber ? episode - 1 : null;
            }
        }

        return last;
    }

    private static PackFileEntry Skipped(string relativePath, string fileName, string reason) =>
        new()
        {
            RelativePath = relativePath,
            FileName = fileName,
            Classification = PackFileClassification.Skipped,
            MatchReason = reason
        };

    // The season group already ranks SxxEyy in the file name above the folder (PARSE-04).
    private static int? ResolveSeason(
        string relativePath,
        TorrentCandidateParseResult parsed,
        int? pathSeasonHint,
        IReadOnlyList<PackSeasonFileGrouper.SeasonFileGroup> seasonGroups)
    {
        var owningGroup = seasonGroups.FirstOrDefault(group =>
            group.Files.Any(file => string.Equals(file.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase)));
        if (owningGroup?.SeasonNumber > 0)
        {
            return owningGroup.SeasonNumber;
        }

        if (pathSeasonHint is > 0)
        {
            return pathSeasonHint;
        }

        return parsed.SeasonNumber;
    }

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
