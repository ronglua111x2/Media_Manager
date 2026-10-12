using System.Text.RegularExpressions;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public static class PackSpecialPayloadBuilder
{
    private static readonly Regex StandardS00ERegex = new(
        @"\bS00E(?<episode>\d{1,4})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static SpecialMappingAIRequest Build(
        IReadOnlyList<(string RelativePath, string FileName)> files,
        PackFolderTreeAnalysis tree,
        IReadOnlyList<TrackedEpisode> specialsEpisodes,
        string showTitle,
        int tmdbId)
    {
        var context = BuildContext(files, tree, specialsEpisodes, showTitle, tmdbId);
        var proposals = BuildProposals(context, specialsEpisodes);
        return new SpecialMappingAIRequest
        {
            Context = context,
            Proposals = proposals,
            Task = "finalize"
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
            items.Add(new SpecialMappingItem
            {
                RelativePath = candidate.RelativePath,
                Episode = episode,
                Source = source,
                ProposalReason = proposal.Reason,
                Reason = BuildReason(proposal.Reason, episode, source)
            });
        }

        foreach (var candidate in request.Context.Candidates)
        {
            if (items.Any(item => string.Equals(item.RelativePath, candidate.RelativePath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            items.Add(new SpecialMappingItem
            {
                RelativePath = candidate.RelativePath,
                Episode = null,
                Source = source,
                ProposalReason = SpecialMappingProposalReason.None,
                Reason = "No proposal generated for special/OVA file."
            });
        }

        if (request.Context.CountMismatch)
        {
            warnings.Add(
                $"{request.Context.CandidateCount} torrent special file(s) vs {request.Context.TmdbSpecialCount} TMDB S00 episode(s).");
        }

        return BuildResult(items, warnings, usedGemini: source == SpecialMappingSource.Gemini);
    }

    public static SpecialMappingResult BuildFromAiResponse(
        SpecialMappingAIRequest request,
        SpecialMappingAIResponse response,
        IReadOnlyList<TrackedEpisode> specialsEpisodes)
    {
        var episodesByNumber = specialsEpisodes
            .Where(episode => episode.SeasonNumber == AppConstants.SpecialsSeasonNumber)
            .ToDictionary(episode => episode.EpisodeNumber);

        var items = new List<SpecialMappingItem>();
        var mappedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (candidateIndex, s00e) in response.Mappings)
        {
            if (candidateIndex < 0 || candidateIndex >= request.Context.Candidates.Count)
            {
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

        var warnings = response.Warnings.ToList();
        if (request.Context.CountMismatch &&
            !warnings.Any(warning => warning.Contains("vs", StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add(
                $"{request.Context.CandidateCount} torrent special file(s) vs {request.Context.TmdbSpecialCount} TMDB S00 episode(s).");
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
                .ThenBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
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
            .ThenBy(candidate => candidate.RelativePath, StringComparer.OrdinalIgnoreCase)
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

    private static IReadOnlyList<SpecialMappingProposal> BuildProposals(
        SpecialMappingContext context,
        IReadOnlyList<TrackedEpisode> specialsEpisodes)
    {
        if (context.Candidates.Count == 0)
        {
            return [];
        }

        var proposals = new List<SpecialMappingProposal>();
        var claimed = new HashSet<int>();
        var tmdbOrdered = specialsEpisodes
            .Where(episode => episode.SeasonNumber == AppConstants.SpecialsSeasonNumber)
            .OrderBy(episode => episode.AirDate ?? DateTime.MaxValue)
            .ThenBy(episode => episode.EpisodeNumber)
            .ToList();
        var proposedCandidates = new HashSet<int>();

        for (var index = 0; index < context.Candidates.Count; index++)
        {
            var candidate = context.Candidates[index];
            if (candidate.NamingPattern != SpecialMappingNamingPattern.StandardS00E)
            {
                continue;
            }

            var directEpisode = TryGetDirectS00E(candidate);
            if (directEpisode is null || claimed.Contains(directEpisode.Value))
            {
                continue;
            }

            claimed.Add(directEpisode.Value);
            proposedCandidates.Add(index);
            proposals.Add(new SpecialMappingProposal
            {
                CandidateIndex = index,
                ProposedS00E = directEpisode.Value,
                Reason = SpecialMappingProposalReason.Direct
            });
        }

        var flattenCandidates = context.Candidates
            .Select((candidate, index) => (candidate, index))
            .Where(item => item.candidate.NamingPattern == SpecialMappingNamingPattern.SnSnn)
            .Where(item => !proposedCandidates.Contains(item.index))
            .OrderBy(item => item.candidate.GlobalSortOrder)
            .ToList();

        var flattenCursor = 0;
        foreach (var (_, index) in flattenCandidates)
        {
            while (flattenCursor < tmdbOrdered.Count && claimed.Contains(tmdbOrdered[flattenCursor].EpisodeNumber))
            {
                flattenCursor++;
            }

            if (flattenCursor >= tmdbOrdered.Count)
            {
                break;
            }

            var episode = tmdbOrdered[flattenCursor];
            claimed.Add(episode.EpisodeNumber);
            proposedCandidates.Add(index);
            proposals.Add(new SpecialMappingProposal
            {
                CandidateIndex = index,
                ProposedS00E = episode.EpisodeNumber,
                Reason = SpecialMappingProposalReason.Flatten
            });
            flattenCursor++;
        }

        for (var index = 0; index < context.Candidates.Count; index++)
        {
            if (proposedCandidates.Contains(index))
            {
                continue;
            }

            var candidate = context.Candidates[index];
            if (candidate.NamingPattern != SpecialMappingNamingPattern.OvaDash)
            {
                continue;
            }

            var localEpisode = candidate.LocalIndex;
            if (localEpisode <= 0 || claimed.Contains(localEpisode))
            {
                continue;
            }

            if (tmdbOrdered.All(episode => episode.EpisodeNumber != localEpisode))
            {
                continue;
            }

            claimed.Add(localEpisode);
            proposedCandidates.Add(index);
            proposals.Add(new SpecialMappingProposal
            {
                CandidateIndex = index,
                ProposedS00E = localEpisode,
                Reason = SpecialMappingProposalReason.Direct
            });
        }

        var titleCandidates = specialsEpisodes
            .Where(episode => episode.SeasonNumber == AppConstants.SpecialsSeasonNumber)
            .OrderBy(episode => episode.EpisodeNumber)
            .ToList();

        for (var index = 0; index < context.Candidates.Count; index++)
        {
            if (proposedCandidates.Contains(index))
            {
                continue;
            }

            var candidate = context.Candidates[index];
            if (candidate.NamingPattern is SpecialMappingNamingPattern.SnSnn or SpecialMappingNamingPattern.Opaque)
            {
                continue;
            }

            var parsed = TorrentCandidateParser.Parse(candidate.FileName, candidate.RelativePath);
            var titleMatch = SpecialEpisodeTitleMatcher.TryMatch(parsed, titleCandidates, claimed);
            if (titleMatch is null)
            {
                continue;
            }

            claimed.Add(titleMatch.EpisodeNumber);
            proposedCandidates.Add(index);
            proposals.Add(new SpecialMappingProposal
            {
                CandidateIndex = index,
                ProposedS00E = titleMatch.EpisodeNumber,
                Reason = SpecialMappingProposalReason.Title
            });
        }

        if (context.CandidateCount == context.TmdbSpecialCount)
        {
            var remaining = context.Candidates
                .Select((candidate, candidateIndex) => (candidate, candidateIndex))
                .Where(item => !proposedCandidates.Contains(item.candidateIndex))
                .OrderBy(item => item.candidate.GlobalSortOrder)
                .ToList();

            var ordinalCursor = 0;
            foreach (var (_, candidateIndex) in remaining)
            {
                while (ordinalCursor < tmdbOrdered.Count && claimed.Contains(tmdbOrdered[ordinalCursor].EpisodeNumber))
                {
                    ordinalCursor++;
                }

                if (ordinalCursor >= tmdbOrdered.Count)
                {
                    break;
                }

                var episode = tmdbOrdered[ordinalCursor];
                claimed.Add(episode.EpisodeNumber);
                proposedCandidates.Add(candidateIndex);
                proposals.Add(new SpecialMappingProposal
                {
                    CandidateIndex = candidateIndex,
                    ProposedS00E = episode.EpisodeNumber,
                    Reason = SpecialMappingProposalReason.Ordinal
                });
                ordinalCursor++;
            }
        }

        return proposals;
    }

    private static (int LocalIndex, int? ParentSeason, SpecialMappingNamingPattern NamingPattern) BuildCandidateMetadata(
        string relativePath,
        string fileName,
        PackSpecialBucket bucket,
        InferredSpecialPattern pattern)
    {
        var parsed = TorrentCandidateParser.Parse(fileName, relativePath);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var namingPattern = DetectNamingPattern(parsed, pattern);
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

    private static SpecialMappingNamingPattern DetectNamingPattern(
        TorrentCandidateParseResult parsed,
        InferredSpecialPattern pattern)
    {
        if (pattern.Style == InferredSpecialNamingStyle.StandardS00E)
        {
            return SpecialMappingNamingPattern.StandardS00E;
        }

        if (pattern.Style == InferredSpecialNamingStyle.OvaDashNumber)
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

    private static SpecialMappingResult BuildResult(
        IReadOnlyList<SpecialMappingItem> items,
        IReadOnlyList<string> warnings,
        bool usedGemini)
    {
        var byPath = items.ToDictionary(
            item => item.RelativePath,
            item => item,
            StringComparer.OrdinalIgnoreCase);

        return new SpecialMappingResult
        {
            Items = items,
            Warnings = warnings,
            UsedGemini = usedGemini,
            ByPath = byPath
        };
    }

    private static string BuildReason(
        SpecialMappingProposalReason reason,
        TrackedEpisode episode,
        SpecialMappingSource source)
    {
        var prefix = source == SpecialMappingSource.Gemini ? "Gemini" : "Rule proposal";
        return reason switch
        {
            SpecialMappingProposalReason.Direct => $"{prefix}: direct S00E{episode.EpisodeNumber:00}.",
            SpecialMappingProposalReason.Flatten => $"{prefix}: global flatten to S00E{episode.EpisodeNumber:00}.",
            SpecialMappingProposalReason.Title => $"{prefix}: title match to S00E{episode.EpisodeNumber:00}.",
            SpecialMappingProposalReason.Ordinal => $"{prefix}: ordinal to S00E{episode.EpisodeNumber:00}.",
            _ => $"{prefix}: mapped to S00E{episode.EpisodeNumber:00}."
        };
    }
}
