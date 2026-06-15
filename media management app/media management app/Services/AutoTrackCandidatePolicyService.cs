using media_management_app.Models;

namespace media_management_app.Services;

public sealed class AutoTrackCandidatePolicyService
{
    public IReadOnlyList<EpisodeFetchCandidate> Apply(
        TrackedShow show,
        AutoTrackSettings settings,
        IReadOnlyList<EpisodeFetchCandidate> candidates)
    {
        var policy = ResolvePolicy(show, settings);
        var filtered = FilterCandidates(policy, candidates);
        return RankCandidates(filtered);
    }

    public ResolvedAutoTrackQualityPolicy ResolvePolicy(TrackedShow show, AutoTrackSettings settings)
    {
        var global = settings.Quality ?? new AutoTrackQualityPolicy();
        return new ResolvedAutoTrackQualityPolicy
        {
            MinQuality = show.AutoTrackMinQuality ?? global.MinQuality,
            MinSeeders = show.AutoTrackMinSeeders ?? global.MinSeeders,
            MinFileSizeMb = show.AutoTrackMinFileSizeMb ?? global.MinFileSizeMb,
            MaxFileSizeMb = show.AutoTrackMaxFileSizeMb ?? global.MaxFileSizeMb,
            AllowedQualities = ParseAllowedQualities(show.AutoTrackAllowedQualities) ??
                               global.AllowedQualities?.Where(quality => !string.IsNullOrWhiteSpace(quality))
                                   .Select(quality => quality.Trim())
                                   .ToList()
        };
    }

    private static IReadOnlyList<EpisodeFetchCandidate> FilterCandidates(
        ResolvedAutoTrackQualityPolicy policy,
        IReadOnlyList<EpisodeFetchCandidate> candidates)
    {
        return candidates.Where(candidate => PassesPolicy(policy, candidate)).ToList();
    }

    private static IReadOnlyList<EpisodeFetchCandidate> RankCandidates(IReadOnlyList<EpisodeFetchCandidate> candidates)
    {
        return candidates
            .OrderByDescending(candidate => candidate.TotalScore)
            .ThenByDescending(candidate => TorrentQuality.GetRank(candidate.QualityLabel))
            .ToList();
    }

    private static bool PassesPolicy(ResolvedAutoTrackQualityPolicy policy, EpisodeFetchCandidate candidate)
    {
        if (policy.MinSeeders > 0 && candidate.Seeders < policy.MinSeeders)
        {
            return false;
        }

        var quality = string.IsNullOrWhiteSpace(candidate.QualityLabel)
            ? TorrentQuality.Detect(candidate.FileName)
            : candidate.QualityLabel;

        if (policy.AllowedQualities is { Count: > 0 } &&
            !TorrentQuality.MatchesSelectedQuality(quality, policy.AllowedQualities))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(policy.MinQuality))
        {
            var minRank = TorrentQuality.GetRank(policy.MinQuality);
            var candidateRank = TorrentQuality.GetRank(quality);
            if (candidateRank > 0 && minRank > 0 && candidateRank < minRank)
            {
                return false;
            }
        }

        if (policy.MinFileSizeMb is > 0)
        {
            var minBytes = policy.MinFileSizeMb.Value * 1024L * 1024L;
            if (candidate.FileSize > 0 && candidate.FileSize < minBytes)
            {
                return false;
            }
        }

        if (policy.MaxFileSizeMb is > 0)
        {
            var maxBytes = policy.MaxFileSizeMb.Value * 1024L * 1024L;
            if (candidate.FileSize > maxBytes)
            {
                return false;
            }
        }

        return true;
    }

    private static List<string>? ParseAllowedQualities(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return null;
        }

        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(quality => !string.IsNullOrWhiteSpace(quality))
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
}
