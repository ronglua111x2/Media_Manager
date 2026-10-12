namespace media_management_app.Models;

public sealed class AutoTrackRunResult
{
    public int ShowsProcessed { get; set; }

    public int EpisodesQueued { get; set; }

    public int CandidatesFound { get; set; }

    public int TorrentsAdded { get; set; }

    public int ReconciledCount { get; set; }

    public int LinkedCount { get; set; }

    public int TmdbRefreshed { get; set; }

    public int Failed { get; set; }

    public bool Succeeded { get; set; }

    public string Summary { get; set; } = string.Empty;

    public List<HuntEpisodeOutcome> EpisodeOutcomes { get; } = [];

    public string FormatHumanSummary()
    {
        return Services.HuntLogFormatter.FormatHumanSummary(Summary, EpisodeOutcomes);
    }
}
