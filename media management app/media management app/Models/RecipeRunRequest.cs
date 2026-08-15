using media_management_app.Common;

namespace media_management_app.Models;

public sealed class RecipeRunRequest
{
    public MediaKind TargetKind { get; init; } = MediaKind.TvEpisode;

    public long? ShowId { get; init; }

    public long? MovieId { get; init; }

    public int? SeasonNumber { get; init; }

    public int? EpisodeNumber { get; init; }

    public string? RecipeId { get; init; }
}

public sealed class RecipeDryRunResult
{
    public SearchRecipe Recipe { get; init; } = new();

    public string TargetTitle { get; init; } = string.Empty;

    public List<string> Queries { get; init; } = [];

    public List<RecipeCandidateResult> AcceptedCandidates { get; init; } = [];

    public List<RecipeCandidateResult> RejectedCandidates { get; init; } = [];

    public RecipeCandidateResult? BestCandidate => AcceptedCandidates
        .OrderByDescending(candidate => candidate.TotalScore)
        .FirstOrDefault();

    public string Summary =>
        $"{AcceptedCandidates.Count} accepted, {RejectedCandidates.Count} rejected, {Queries.Count} quer{(Queries.Count == 1 ? "y" : "ies")}.";
}

public sealed class RecipeCandidateResult
{
    public TorrentSearchResult SearchResult { get; init; } = new();

    public CandidateRejectReason RejectReason { get; init; } = CandidateRejectReason.None;

    public string RejectDetail { get; init; } = string.Empty;

    public int QualityScore { get; init; }

    public int IdentityScore { get; init; }

    public int EpisodeScore { get; init; }

    public int AudioScore { get; init; }

    public int PreferTermsScore { get; init; }

    public int SizeScore { get; init; }

    public int TotalScore { get; init; }

    public bool IsAccepted => RejectReason == CandidateRejectReason.None;

    public string DisplayName =>
        $"{SearchResult.FileName} | {SearchResult.FileSizeDisplay} | {SearchResult.Seeders} seeders | {SearchResult.EngineName}";
}

public enum CandidateRejectReason
{
    None = 0,
    NotAddable = 1,
    PluginError = 2,
    TitleMismatch = 3,
    YearMismatch = 4,
    EpisodeMismatch = 5,
    QualityMismatch = 6,
    SeedersTooLow = 7,
    MissingIncludeTerm = 8,
    ExcludedTerm = 9,
    BlockedReleaseGroup = 10,
    SizeTooLarge = 11,
    SizeTooSmall = 12,
    WrongReleaseKind = 13
}
