using System.Collections.Generic;
using media_management_app.Models;

namespace media_management_app.Services;

public static class AutoTrackPolicyDiagnostics
{
    public static CandidateRejectReason? GetRejectReason(
        int minSeeders,
        string? minQuality,
        IReadOnlyList<string>? allowedQualities,
        int? minFileSizeMb,
        int? maxFileSizeMb,
        int seeders,
        string qualityLabel,
        string fileName,
        long fileSize)
    {
        if (minSeeders > 0 && seeders < minSeeders)
        {
            return CandidateRejectReason.SeedersTooLow;
        }

        var quality = string.IsNullOrWhiteSpace(qualityLabel)
            ? TorrentQuality.Detect(fileName)
            : qualityLabel;

        if (allowedQualities is { Count: > 0 } &&
            !TorrentQuality.MatchesSelectedQuality(quality, allowedQualities))
        {
            return CandidateRejectReason.QualityMismatch;
        }

        if (!string.IsNullOrWhiteSpace(minQuality))
        {
            var minRank = TorrentQuality.GetRank(minQuality);
            var candidateRank = TorrentQuality.GetRank(quality);
            if (candidateRank > 0 && minRank > 0 && candidateRank < minRank)
            {
                return CandidateRejectReason.QualityMismatch;
            }
        }

        if (minFileSizeMb is > 0)
        {
            var minBytes = minFileSizeMb.Value * 1024L * 1024L;
            if (fileSize > 0 && fileSize < minBytes)
            {
                return CandidateRejectReason.SizeTooSmall;
            }
        }

        if (maxFileSizeMb is > 0)
        {
            var maxBytes = maxFileSizeMb.Value * 1024L * 1024L;
            if (fileSize > maxBytes)
            {
                return CandidateRejectReason.SizeTooLarge;
            }
        }

        return null;
    }
}
