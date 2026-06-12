using media_management_app.Common;

namespace media_management_app.Models;

public sealed class SourceItem
{
    public int DisplayIndex { get; set; }

    public string DisplayTitle => MediaKind == MediaKind.Movie
        ? MovieTitle ?? MatchedTitle ?? ShowTitle ?? string.Empty
        : ShowTitle ?? MatchedTitle ?? MovieTitle ?? string.Empty;

    public long Id { get; set; }

    public string SourceRootFolder { get; set; } = string.Empty;

    public string ParentFolder { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string ScanText { get; set; } = string.Empty;

    public MediaKind MediaKind { get; set; } = MediaKind.Unknown;

    public ParserPattern ParserPattern { get; set; } = ParserPattern.Unknown;

    public string? ShowTitle { get; set; }

    public string? MovieTitle { get; set; }

    public int? MovieYear { get; set; }

    public int? SeasonNumber { get; set; }

    public int? EpisodeNumber { get; set; }

    public int? MappedSeasonNumber { get; set; }

    public int? MappedEpisodeNumber { get; set; }

    public string? EpisodeMappingSource { get; set; }

    public double? EpisodeMappingConfidence { get; set; }

    public string? EpisodeMappingReason { get; set; }

    public string? EpisodeTitle { get; set; }

    public string? MatchedTitle { get; set; }

    public int? MatchedYear { get; set; }

    public string? Provider { get; set; }

    public string? ProviderId { get; set; }

    public double? MatchConfidence { get; set; }

    public string? MatchReason { get; set; }

    public bool RequiresManualReview { get; set; }

    public bool MatchAccepted { get; set; }

    public bool UseAbsoluteAnimeMapping { get; set; }

    public ItemState State { get; set; } = ItemState.Discovered;

    public string? Notes { get; set; }

    public string? LinkedPath { get; set; }

    public AutoTorrentLinkKind? AutoTorrentLinkKind { get; set; }

    public string? AutoTorrentTorrentHash { get; set; }

    public int? AutoTorrentPackOwnerSeasonNumber { get; set; }

    public bool IsExternalImport { get; set; }

    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
}
