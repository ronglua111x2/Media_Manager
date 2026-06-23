namespace media_management_app.Models;

public enum PackLinkReviewDecision
{
    Cancel,
    Retry,
    Accept
}

public sealed class SeasonPackLinkPreview
{
    public required TrackedShow Show { get; init; }

    public required TrackedSeason OwnerSeason { get; init; }

    public required PackTorrentInventory Inventory { get; init; }

    public required SpecialMappingResult SpecialMappings { get; init; }

    public bool RequiresReview { get; init; }

    public int RegularEpisodeCount { get; init; }

    public int MatchedSpecialCount { get; init; }

    public int OrphanExtraCount { get; init; }

    public IReadOnlyDictionary<int, SeasonRegularEpisodeStats> RegularEpisodesBySeason { get; init; } =
        new Dictionary<int, SeasonRegularEpisodeStats>();
}
