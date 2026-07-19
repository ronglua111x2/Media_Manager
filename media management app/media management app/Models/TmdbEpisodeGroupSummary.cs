namespace media_management_app.Models;

public sealed class TmdbEpisodeGroupSummary
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int Type { get; init; }

    public int EpisodeCount { get; init; }

    public int GroupCount { get; init; }

    public string SummaryLabel => $"{GroupCount} season{(GroupCount == 1 ? string.Empty : "s")} · {EpisodeCount} episode{(EpisodeCount == 1 ? string.Empty : "s")}";
}
