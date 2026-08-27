namespace media_management_app.Models;

public sealed class SnapshotMatchResult
{
    public bool IsAccepted { get; init; }

    public string? RejectReason { get; init; }

    public CandidateRejectReason RejectReasonCode { get; init; }

    public string RejectDetail { get; init; } = string.Empty;

    public int TotalScore { get; init; }

    public int QualityScore { get; init; }

    public int AudioScore { get; init; }

    public int PreferTermsScore { get; init; }

    public int SizeScore { get; init; }

    public int IdentityScore { get; init; }

    public int EpisodeScore { get; init; }

    public RecipeCandidateResult ToRecipeCandidateResult(TorrentSearchResult result)
    {
        return new RecipeCandidateResult
        {
            SearchResult = result,
            RejectReason = IsAccepted ? CandidateRejectReason.None : RejectReasonCode,
            RejectDetail = IsAccepted
                ? string.Empty
                : (string.IsNullOrWhiteSpace(RejectDetail) ? RejectReason ?? string.Empty : RejectDetail),
            QualityScore = QualityScore,
            IdentityScore = IdentityScore,
            EpisodeScore = EpisodeScore,
            AudioScore = AudioScore,
            PreferTermsScore = PreferTermsScore,
            SizeScore = SizeScore,
            TotalScore = TotalScore
        };
    }
}
