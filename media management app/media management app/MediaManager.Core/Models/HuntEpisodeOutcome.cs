using System.Collections.Generic;

namespace media_management_app.Models;

public enum HuntEpisodeStage
{
    Search = 0,
    RecipeMatch = 1,
    AutoTrackPolicy = 2,
    Accept = 3,
    Add = 4,
    Succeeded = 5
}

public sealed class HuntEpisodeOutcome
{
    public long OrderId { get; set; }

    public string ShowTitle { get; set; } = string.Empty;

    public string EpisodeLabel { get; set; } = string.Empty;

    public int SearchRows { get; set; }

    public int RecipeMatched { get; set; }

    public int PolicyKept { get; set; }

    public HuntEpisodeStage Stage { get; set; }

    public string? FailureReason { get; set; }

    public string? FailureDetail { get; set; }

    public Dictionary<CandidateRejectReason, int> PolicyRejectCounts { get; set; } = [];

    public int? PolicyMinFileSizeMb { get; set; }

    public int? PolicyMinSeeders { get; set; }

    public string? PolicyMinQuality { get; set; }

    public long? BestRejectedFileSize { get; set; }

    public string? AddedCandidateName { get; set; }

    public bool IsFailure => Stage != HuntEpisodeStage.Succeeded;
}
