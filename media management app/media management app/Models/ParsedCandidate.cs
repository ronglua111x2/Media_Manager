using media_management_app.Common;

namespace media_management_app.Models;

public sealed class ParsedCandidate
{
    public string SourceText { get; set; } = string.Empty;

    public MediaKind MediaKind { get; set; } = MediaKind.Unknown;

    public ParserPattern ParserPattern { get; set; } = ParserPattern.Unknown;

    public string? ShowTitle { get; set; }

    public string? MovieTitle { get; set; }

    public int? MovieYear { get; set; }

    public int? SeasonNumber { get; set; }

    public int? EpisodeNumber { get; set; }

    /// <summary>Last episode of a multi-episode file (S01E01E02, S01E01-E02, S01E01-02); null for one episode.</summary>
    public int? LastEpisodeNumber { get; set; }

    public string? EpisodeTitle { get; set; }

    public bool NeedsReview { get; set; }

    public string? Reason { get; set; }
}
