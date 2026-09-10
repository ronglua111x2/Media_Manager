namespace media_management_app.Models;

public enum HuntEpisodeStage
{
    Search = 0,
    RecipeMatch = 1,
    Accept = 2,
    Add = 3,
    Succeeded = 4
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

    public string? AddedCandidateName { get; set; }

    public bool IsFailure => Stage != HuntEpisodeStage.Succeeded;
}
