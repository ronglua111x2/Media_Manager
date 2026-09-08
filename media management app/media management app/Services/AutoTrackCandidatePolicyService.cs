using media_management_app.Models;

namespace media_management_app.Services;

public sealed class AutoTrackCandidatePolicyService
{
    public IReadOnlyList<EpisodeFetchCandidate> Apply(
        TrackedShow show,
        AutoTrackSettings settings,
        IReadOnlyList<EpisodeFetchCandidate> candidates)
    {
        return ApplyWithDiagnostics(show, settings, candidates).Kept;
    }

    public AutoTrackPolicyApplyResult ApplyWithDiagnostics(
        TrackedShow show,
        AutoTrackSettings settings,
        IReadOnlyList<EpisodeFetchCandidate> candidates)
    {
        var policy = ResolvePolicy(show, settings);
        var kept = new List<EpisodeFetchCandidate>();
        var rejectCounts = new Dictionary<CandidateRejectReason, int>();
        EpisodeFetchCandidate? bestRejected = null;

        foreach (var candidate in candidates)
        {
            var reason = GetPolicyRejectReason(policy, candidate);
            if (reason is null)
            {
                kept.Add(candidate);
                continue;
            }

            rejectCounts[reason.Value] = rejectCounts.GetValueOrDefault(reason.Value) + 1;
            if (bestRejected is null || candidate.FileSize > bestRejected.FileSize)
            {
                bestRejected = candidate;
            }
        }

        return new AutoTrackPolicyApplyResult
        {
            Kept = RankCandidates(kept),
            RejectCounts = rejectCounts,
            Policy = policy,
            BestRejected = bestRejected
        };
    }

    public ResolvedAutoTrackQualityPolicy ResolvePolicy(TrackedShow show, AutoTrackSettings settings)
    {
        _ = show;
        var global = settings.Quality ?? new AutoTrackQualityPolicy();
        return new ResolvedAutoTrackQualityPolicy
        {
            MinQuality = global.MinQuality,
            MinSeeders = global.MinSeeders,
            MinFileSizeMb = global.MinFileSizeMb,
            MaxFileSizeMb = global.MaxFileSizeMb,
            AllowedQualities = global.AllowedQualities?.Where(quality => !string.IsNullOrWhiteSpace(quality))
                .Select(quality => quality.Trim())
                .ToList()
        };
    }

    public static CandidateRejectReason? GetPolicyRejectReason(
        ResolvedAutoTrackQualityPolicy policy,
        EpisodeFetchCandidate candidate)
    {
        return AutoTrackPolicyDiagnostics.GetRejectReason(
            policy.MinSeeders,
            policy.MinQuality,
            policy.AllowedQualities,
            policy.MinFileSizeMb,
            policy.MaxFileSizeMb,
            candidate.Seeders,
            candidate.QualityLabel,
            candidate.FileName,
            candidate.FileSize);
    }

    private static IReadOnlyList<EpisodeFetchCandidate> RankCandidates(IReadOnlyList<EpisodeFetchCandidate> candidates)
    {
        return candidates
            .OrderByDescending(candidate => candidate.TotalScore)
            .ThenByDescending(candidate => TorrentQuality.GetRank(candidate.QualityLabel))
            .ToList();
    }

    public sealed class ResolvedAutoTrackQualityPolicy
    {
        public string? MinQuality { get; init; }

        public int MinSeeders { get; init; }

        public int? MinFileSizeMb { get; init; }

        public int? MaxFileSizeMb { get; init; }

        public IReadOnlyList<string>? AllowedQualities { get; init; }
    }

    public sealed class AutoTrackPolicyApplyResult
    {
        public IReadOnlyList<EpisodeFetchCandidate> Kept { get; init; } = [];

        public IReadOnlyDictionary<CandidateRejectReason, int> RejectCounts { get; init; } =
            new Dictionary<CandidateRejectReason, int>();

        public ResolvedAutoTrackQualityPolicy Policy { get; init; } = new();

        public EpisodeFetchCandidate? BestRejected { get; init; }
    }
}
