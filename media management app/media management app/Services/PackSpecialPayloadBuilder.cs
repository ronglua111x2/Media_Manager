using System.Text.RegularExpressions;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public static class PackSpecialPayloadBuilder
{
    private static readonly Regex StandardS00ERegex = new(
        @"\bS00E(?<episode>\d{1,4})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex OvaWordRegex = new(
        @"\b(?:OVA|OAD)s?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <param name="regularEpisodes">
    /// Episodes of the regular seasons. Their air dates split TMDB Season 0 into per-season windows for the
    /// deterministic tier; without them that tier is skipped.
    /// </param>
    public static SpecialMappingAIRequest Build(
        IReadOnlyList<(string RelativePath, string FileName)> files,
        PackFolderTreeAnalysis tree,
        IReadOnlyList<TrackedEpisode> specialsEpisodes,
        string showTitle,
        int tmdbId,
        IReadOnlyList<TrackedEpisode>? regularEpisodes = null)
    {
        var context = BuildContext(files, tree, specialsEpisodes, showTitle, tmdbId);
        var tier = BuildProposals(context, specialsEpisodes, regularEpisodes ?? []);
        return new SpecialMappingAIRequest
        {
            Context = context,
            Proposals = tier.Proposals,
            Task = "finalize",
            Suggestions = tier.Suggestions,
            UnresolvedReasons = tier.UnresolvedReasons
        };
    }

    public static SpecialMappingResult ApplyProposals(
        SpecialMappingAIRequest request,
        IReadOnlyList<TrackedEpisode> specialsEpisodes,
        SpecialMappingSource source)
    {
        var episodesByNumber = specialsEpisodes
            .Where(episode => episode.SeasonNumber == AppConstants.SpecialsSeasonNumber)
            .ToDictionary(episode => episode.EpisodeNumber);

        var claimed = new HashSet<int>();
        var items = new List<SpecialMappingItem>();
        var warnings = new List<string>();

        foreach (var proposal in request.Proposals.OrderBy(item => item.CandidateIndex))
        {
            if (proposal.CandidateIndex < 0 || proposal.CandidateIndex >= request.Context.Candidates.Count)
            {
                continue;
            }

            var candidate = request.Context.Candidates[proposal.CandidateIndex];
            episodesByNumber.TryGetValue(proposal.ProposedS00E, out var episode);
            if (episode is null || claimed.Contains(episode.EpisodeNumber))
            {
                items.Add(new SpecialMappingItem
                {
                    RelativePath = candidate.RelativePath,
                    Episode = null,
                    Source = source,
                    ProposalReason = proposal.Reason,
                    Reason = $"Proposal S00E{proposal.ProposedS00E:00} could not be applied."
                });
                continue;
            }

            claimed.Add(episode.EpisodeNumber);
            var titleConfirmed = SpecialEpisodeTitleMatcher.Confirms(candidate.FileName, request.Context.ShowTitle, episode);
            items.Add(new SpecialMappingItem
            {
                RelativePath = candidate.RelativePath,
                Episode = episode,
                Source = source,
                ProposalReason = proposal.Reason,
                Reason = BuildReason(proposal.Reason, episode, source, titleConfirmed),
                TitleConfirmed = titleConfirmed
            });
        }

        for (var index = 0; index < request.Context.Candidates.Count; index++)
        {
            var candidate = request.Context.Candidates[index];
            if (items.Any(item => string.Equals(item.RelativePath, candidate.RelativePath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            request.Suggestions.TryGetValue(index, out var suggestion);
            if (suggestion is not null && claimed.Contains(suggestion.EpisodeNumber))
            {
                suggestion = null;
            }

            items.Add(new SpecialMappingItem
            {
                RelativePath = candidate.RelativePath,
                Episode = null,
                Source = source,
                ProposalReason = SpecialMappingProposalReason.None,
                Reason = BuildUnmappedReason(request.UnresolvedReasons.GetValueOrDefault(index), suggestion),
                SuggestedEpisode = suggestion
            });
        }

        if (request.Context.CountMismatch)
        {
            warnings.Add(CountMismatchWarning(request.Context));
        }

        return BuildResult(items, warnings, usedGemini: source == SpecialMappingSource.Gemini);
    }

    public static SpecialMappingResult BuildFromAiResponse(
        SpecialMappingAIRequest request,
        SpecialMappingAIResponse response,
        IReadOnlyList<TrackedEpisode> specialsEpisodes,
        IReadOnlyList<string>? extraWarnings = null)
    {
        var episodesByNumber = specialsEpisodes
            .Where(episode => episode.SeasonNumber == AppConstants.SpecialsSeasonNumber)
            .ToDictionary(episode => episode.EpisodeNumber);

        var items = new List<SpecialMappingItem>();
        var mappedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = response.Warnings.ToList();

        foreach (var (candidateIndex, s00e) in response.Mappings)
        {
            if (candidateIndex < 0 || candidateIndex >= request.Context.Candidates.Count)
            {
                warnings.Add(
                    $"Gemini returned candidate index {candidateIndex}, outside 0..{request.Context.Candidates.Count - 1}; ignored.");
                continue;
            }

            var candidate = request.Context.Candidates[candidateIndex];
            mappedPaths.Add(candidate.RelativePath);
            episodesByNumber.TryGetValue(s00e, out var episode);
            items.Add(new SpecialMappingItem
            {
                RelativePath = candidate.RelativePath,
                Episode = episode,
                Source = SpecialMappingSource.Gemini,
                Reason = episode is null
                    ? $"Gemini mapped to missing TMDB S00E{s00e:00}."
                    : $"Gemini mapped to TMDB S00E{episode.EpisodeNumber:00}."
            });
        }

        foreach (var candidate in request.Context.Candidates)
        {
            if (mappedPaths.Contains(candidate.RelativePath))
            {
                continue;
            }

            items.Add(new SpecialMappingItem
            {
                RelativePath = candidate.RelativePath,
                Episode = null,
                Source = SpecialMappingSource.Gemini,
                Reason = "Gemini did not map this special/OVA file."
            });
        }

        if (extraWarnings is not null)
        {
            warnings.AddRange(extraWarnings);
        }

        if (request.Context.CountMismatch)
        {
            var countWarning = CountMismatchWarning(request.Context);
            if (!warnings.Contains(countWarning, StringComparer.Ordinal))
            {
                warnings.Add(countWarning);
            }
        }

        return BuildResult(items, warnings, usedGemini: true);
    }

    private static SpecialMappingContext BuildContext(
        IReadOnlyList<(string RelativePath, string FileName)> files,
        PackFolderTreeAnalysis tree,
        IReadOnlyList<TrackedEpisode> specialsEpisodes,
        string showTitle,
        int tmdbId)
    {
        var buckets = PackSpecialBucketDetector.Detect(files, tree);
        var candidates = new List<SpecialMappingCandidate>();
        var bucketPatterns = new Dictionary<(SpecialLayoutKind Kind, int? ParentSeason), InferredSpecialPattern>();

        foreach (var bucket in buckets.OrderBy(item => item.ParentSeasonNumber ?? 0).ThenBy(item => item.LayoutKind))
        {
            var stems = bucket.Files
                .Select(file => Path.GetFileNameWithoutExtension(file.FileName))
                .Where(stem => !string.IsNullOrWhiteSpace(stem))
                .ToList();
            var pattern = PackSpecialPatternInferrer.Infer(stems, specialsEpisodes.Count);
            bucketPatterns[(bucket.LayoutKind, bucket.ParentSeasonNumber)] = pattern;

            var orderedFiles = bucket.Files
                .Select(file =>
                {
                    var metadata = BuildCandidateMetadata(file.RelativePath, file.FileName, bucket, pattern);
                    return (file.RelativePath, file.FileName, metadata);
                })
                .OrderBy(file => file.metadata.LocalIndex)
                .ThenBy(file => file.FileName, NaturalStringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var (relativePath, fileName, metadata) in orderedFiles)
            {
                candidates.Add(new SpecialMappingCandidate
                {
                    RelativePath = relativePath,
                    FileName = fileName,
                    ParentSeason = metadata.ParentSeason,
                    LocalIndex = metadata.LocalIndex,
                    GlobalSortOrder = 0,
                    LayoutKind = bucket.LayoutKind,
                    NamingPattern = metadata.NamingPattern
                });
            }
        }

        var orderedCandidates = candidates
            .OrderBy(candidate => candidate.ParentSeason ?? 0)
            .ThenBy(candidate => candidate.LocalIndex)
            .ThenBy(candidate => candidate.RelativePath, NaturalStringComparer.OrdinalIgnoreCase)
            .Select((candidate, index) => new SpecialMappingCandidate
            {
                RelativePath = candidate.RelativePath,
                FileName = candidate.FileName,
                ParentSeason = candidate.ParentSeason,
                LocalIndex = candidate.LocalIndex,
                GlobalSortOrder = index + 1,
                LayoutKind = candidate.LayoutKind,
                NamingPattern = candidate.NamingPattern
            })
            .ToList();

        var blocks = orderedCandidates
            .Where(candidate => candidate.ParentSeason is > 0)
            .GroupBy(candidate => candidate.ParentSeason!.Value)
            .OrderBy(group => group.Key)
            .Select(group => (
                ParentSeason: group.Key,
                FileCount: group.Count(),
                LocalIndices: (IReadOnlyList<int>)group.Select(candidate => candidate.LocalIndex).Order().ToList()))
            .ToList();

        var tmdbSpecials = specialsEpisodes
            .Where(episode => episode.SeasonNumber == AppConstants.SpecialsSeasonNumber)
            .OrderBy(episode => episode.AirDate ?? DateTime.MaxValue)
            .ThenBy(episode => episode.EpisodeNumber)
            .Select(episode => (
                Episode: episode.EpisodeNumber,
                AirDate: episode.AirDate?.ToString("yyyy-MM-dd") ?? string.Empty,
                Title: episode.Title))
            .ToList();

        return new SpecialMappingContext
        {
            ShowTitle = showTitle,
            TmdbId = tmdbId,
            Blocks = blocks,
            Candidates = orderedCandidates,
            TmdbSpecials = tmdbSpecials,
            CandidateCount = orderedCandidates.Count,
            TmdbSpecialCount = tmdbSpecials.Count,
            CountMismatch = orderedCandidates.Count != tmdbSpecials.Count,
            HasOpaqueNames = orderedCandidates.Any(candidate => candidate.NamingPattern == SpecialMappingNamingPattern.Opaque)
        };
    }

    /// <summary>
    /// The deterministic tier, in order: <c>S00Exx</c> in the file name (Direct), the season air-date window
    /// (exact-count blocks only), <c>OVA - N</c> when TMDB S00EN is titled OVA N, then the title matcher for any
    /// file. The old global Flatten and whole-pack Ordinal rules are gone (PARSE-09): what the window mapper cannot
    /// place exactly is only suggested for review and left for Gemini.
    /// </summary>
    private static (
        IReadOnlyList<SpecialMappingProposal> Proposals,
        IReadOnlyDictionary<int, TrackedEpisode> Suggestions,
        IReadOnlyDictionary<int, string> UnresolvedReasons) BuildProposals(
        SpecialMappingContext context,
        IReadOnlyList<TrackedEpisode> specialsEpisodes,
        IReadOnlyList<TrackedEpisode> regularEpisodes)
    {
        var noSuggestions = new Dictionary<int, TrackedEpisode>();
        var noReasons = new Dictionary<int, string>();
        if (context.Candidates.Count == 0)
        {
            return ([], noSuggestions, noReasons);
        }

        var specials = specialsEpisodes
            .Where(episode => episode.SeasonNumber == AppConstants.SpecialsSeasonNumber)
            .OrderBy(episode => episode.EpisodeNumber)
            .ToList();
        var specialsByNumber = specials
            .GroupBy(episode => episode.EpisodeNumber)
            .ToDictionary(group => group.Key, group => group.First());
        var proposals = new List<SpecialMappingProposal>();
        var claimed = new HashSet<int>();
        var proposedCandidates = new HashSet<int>();
        var unresolved = new Dictionary<int, string>();

        void Propose(int candidateIndex, int episodeNumber, SpecialMappingProposalReason reason)
        {
            claimed.Add(episodeNumber);
            proposedCandidates.Add(candidateIndex);
            proposals.Add(new SpecialMappingProposal
            {
                CandidateIndex = candidateIndex,
                ProposedS00E = episodeNumber,
                Reason = reason
            });
        }

        for (var index = 0; index < context.Candidates.Count; index++)
        {
            var candidate = context.Candidates[index];
            if (candidate.NamingPattern != SpecialMappingNamingPattern.StandardS00E)
            {
                continue;
            }

            var directEpisode = TryGetDirectS00E(candidate);
            if (directEpisode is null)
            {
                continue;
            }

            if (!specialsByNumber.ContainsKey(directEpisode.Value))
            {
                unresolved[index] = $"File name says S00E{directEpisode.Value:00}, which is not in TMDB Season 0.";
                continue;
            }

            if (claimed.Contains(directEpisode.Value))
            {
                unresolved[index] = $"File name says S00E{directEpisode.Value:00}, which another file already claims.";
                continue;
            }

            Propose(index, directEpisode.Value, SpecialMappingProposalReason.Direct);
        }

        var blocks = SpecialSeasonWindowMapper.BuildBlocks(regularEpisodes, specials);
        var window = SpecialSeasonWindowMapper.Map(context.Candidates, blocks, proposedCandidates, claimed);
        foreach (var assignment in window.Assignments)
        {
            Propose(
                assignment.CandidateIndex,
                assignment.Episode.EpisodeNumber,
                assignment.IsInferred ? SpecialMappingProposalReason.Inferred : SpecialMappingProposalReason.Window);
        }

        for (var index = 0; index < context.Candidates.Count; index++)
        {
            var candidate = context.Candidates[index];
            if (proposedCandidates.Contains(index) ||
                candidate.NamingPattern != SpecialMappingNamingPattern.OvaDash ||
                candidate.LocalIndex <= 0 ||
                claimed.Contains(candidate.LocalIndex) ||
                !specialsByNumber.TryGetValue(candidate.LocalIndex, out var ovaEpisode) ||
                !TitleNamesOva(ovaEpisode.Title, candidate.LocalIndex))
            {
                continue;
            }

            Propose(index, candidate.LocalIndex, SpecialMappingProposalReason.OvaNumber);
        }

        for (var index = 0; index < context.Candidates.Count; index++)
        {
            if (proposedCandidates.Contains(index))
            {
                continue;
            }

            var candidate = context.Candidates[index];
            var titleMatch = SpecialEpisodeTitleMatcher.TryMatch(candidate.FileName, context.ShowTitle, specials, claimed);
            if (titleMatch is not null)
            {
                Propose(index, titleMatch.EpisodeNumber, SpecialMappingProposalReason.Title);
            }
        }

        var suggestions = window.Suggestions
            .Where(pair => !proposedCandidates.Contains(pair.Key) && !claimed.Contains(pair.Value.EpisodeNumber))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var (index, reason) in window.UnresolvedReasons)
        {
            if (!proposedCandidates.Contains(index))
            {
                unresolved.TryAdd(index, reason);
            }
        }

        foreach (var index in proposedCandidates)
        {
            unresolved.Remove(index);
        }

        return (proposals, suggestions, unresolved);
    }

    /// <summary>TMDB title such as "OVA 2: Beach Day" or "OAD #2" for OVA number <paramref name="ovaNumber"/>.</summary>
    private static bool TitleNamesOva(string? title, int ovaNumber) =>
        !string.IsNullOrWhiteSpace(title) &&
        OvaWordRegex.IsMatch(title) &&
        Regex.IsMatch(title, $@"(?<!\d)0*{ovaNumber}(?!\d)");

    private static (int LocalIndex, int? ParentSeason, SpecialMappingNamingPattern NamingPattern) BuildCandidateMetadata(
        string relativePath,
        string fileName,
        PackSpecialBucket bucket,
        InferredSpecialPattern pattern)
    {
        var parsed = TorrentCandidateParser.Parse(fileName, relativePath);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var namingPattern = DetectNamingPattern(parsed, stem, pattern);
        var localIndex = namingPattern switch
        {
            SpecialMappingNamingPattern.SnSnn => parsed.ReleaseSpecialIndex ?? 0,
            SpecialMappingNamingPattern.StandardS00E => TryGetDirectS00EFromStem(stem) ?? 0,
            SpecialMappingNamingPattern.OvaDash => PackSpecialPatternInferrer.TryExtractIndex(stem, pattern) ?? 0,
            _ => PackSpecialPatternInferrer.TryExtractIndex(stem, pattern) ??
                 parsed.ReleaseSpecialIndex ??
                 TorrentCandidateParser.TryParseBareEpisodeIndex(fileName) ??
                 0
        };

        return (localIndex, bucket.ParentSeasonNumber ?? parsed.ReleaseSeasonHint, namingPattern);
    }

    /// <summary>
    /// Naming style of one file. The bucket style (decided by half of its files) only applies to a file that
    /// itself has that shape, so one odd file does not inherit "OVA - N" or "S00Exx" from its neighbours (note R8).
    /// </summary>
    private static SpecialMappingNamingPattern DetectNamingPattern(
        TorrentCandidateParseResult parsed,
        string stem,
        InferredSpecialPattern pattern)
    {
        if (pattern.Style == InferredSpecialNamingStyle.StandardS00E && TryGetDirectS00EFromStem(stem) is not null)
        {
            return SpecialMappingNamingPattern.StandardS00E;
        }

        if (pattern.Style == InferredSpecialNamingStyle.OvaDashNumber &&
            PackSpecialPatternInferrer.TryExtractIndex(stem, pattern) is not null)
        {
            return SpecialMappingNamingPattern.OvaDash;
        }

        if (parsed.ReleaseSeasonHint is > 0 && parsed.ReleaseSpecialIndex is > 0)
        {
            return SpecialMappingNamingPattern.SnSnn;
        }

        if (parsed.IsSpecialContent && parsed.SeasonNumber == AppConstants.SpecialsSeasonNumber && parsed.EpisodeNumber is > 0)
        {
            return SpecialMappingNamingPattern.StandardS00E;
        }

        return SpecialMappingNamingPattern.Opaque;
    }

    private static int? TryGetDirectS00E(SpecialMappingCandidate candidate)
    {
        var stem = Path.GetFileNameWithoutExtension(candidate.FileName);
        return TryGetDirectS00EFromStem(stem);
    }

    private static int? TryGetDirectS00EFromStem(string stem)
    {
        var match = StandardS00ERegex.Match(stem);
        return match.Success && int.TryParse(match.Groups["episode"].Value, out var episode)
            ? episode
            : null;
    }

    private static string CountMismatchWarning(SpecialMappingContext context) =>
        $"{context.CandidateCount} torrent special file(s) vs {context.TmdbSpecialCount} TMDB S00 episode(s).";

    /// <summary>
    /// One item per file. A file that came back with two different S00 episodes (Gemini repeated a
    /// candidateIndex) is left unmapped with a warning instead of throwing.
    /// </summary>
    private static SpecialMappingResult BuildResult(
        IReadOnlyList<SpecialMappingItem> items,
        IReadOnlyList<string> warnings,
        bool usedGemini)
    {
        var merged = new List<SpecialMappingItem>();
        var allWarnings = warnings.ToList();
        var hasConflicts = false;

        foreach (var group in items.GroupBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var episodes = group
                .Where(item => item.Episode is not null)
                .Select(item => item.Episode!.EpisodeNumber)
                .Distinct()
                .ToList();
            if (episodes.Count <= 1)
            {
                merged.Add(group.FirstOrDefault(item => item.Episode is not null) ?? group.First());
                continue;
            }

            hasConflicts = true;
            var first = group.First();
            var fileName = Path.GetFileName(first.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var labels = string.Join(", ", episodes.Select(episode => $"S00E{episode:00}"));
            var message = $"'{fileName}' was mapped more than once ({labels}); left unmapped.";
            allWarnings.Add(message);
            merged.Add(new SpecialMappingItem
            {
                RelativePath = first.RelativePath,
                Episode = null,
                Source = first.Source,
                ProposalReason = first.ProposalReason,
                Reason = message
            });
        }

        return new SpecialMappingResult
        {
            Items = merged,
            Warnings = allWarnings,
            UsedGemini = usedGemini,
            ByPath = merged.ToDictionary(item => item.RelativePath, item => item, StringComparer.OrdinalIgnoreCase),
            RequiresReview = hasConflicts
        };
    }

    private static string BuildReason(
        SpecialMappingProposalReason reason,
        TrackedEpisode episode,
        SpecialMappingSource source,
        bool titleConfirmed)
    {
        var prefix = source == SpecialMappingSource.Gemini ? "Gemini" : "Rule proposal";
        var code = $"S00E{episode.EpisodeNumber:00}";
        return reason switch
        {
            SpecialMappingProposalReason.Direct => titleConfirmed
                ? $"{prefix}: {code} in the file name, title matches TMDB."
                : $"{prefix}: {code} in the file name; no title to confirm it (release numbering may differ from TMDB).",
            SpecialMappingProposalReason.Window => $"{prefix}: special number in its season's air-date window → {code}.",
            SpecialMappingProposalReason.Inferred => $"{prefix}: the only unnumbered special of its season took the only free slot → {code} (inferred).",
            SpecialMappingProposalReason.OvaNumber => $"{prefix}: OVA number matches the TMDB title of {code}.",
            SpecialMappingProposalReason.Flatten => $"{prefix}: global flatten to {code}.",
            SpecialMappingProposalReason.Title => $"{prefix}: title match to {code}.",
            SpecialMappingProposalReason.Ordinal => $"{prefix}: ordinal to {code}.",
            _ => $"{prefix}: mapped to {code}."
        };
    }

    private static string BuildUnmappedReason(string? unresolvedReason, TrackedEpisode? suggestion)
    {
        var reason = unresolvedReason ?? "No rule could place this special/OVA file.";
        return suggestion is null
            ? reason
            : $"{reason} Suggestion (not applied): S00E{suggestion.EpisodeNumber:00} '{suggestion.Title}'.";
    }
}
