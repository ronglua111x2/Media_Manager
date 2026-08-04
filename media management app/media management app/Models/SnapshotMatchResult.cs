namespace media_management_app.Models;

public sealed class SnapshotMatchResult
{
    public bool IsAccepted { get; init; }

    public string? RejectReason { get; init; }

    public int TotalScore { get; init; }

    public int QualityScore { get; init; }

    public int AudioScore { get; init; }

    public int PreferTermsScore { get; init; }

    public int SizeScore { get; init; }

    public int IdentityScore { get; init; }

    public int EpisodeScore { get; init; }
}
