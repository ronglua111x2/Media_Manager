using media_management_app.Services;

namespace media_management_app.Models;

public sealed class SnapshotCandidate
{
    public TorrentSearchResult Result { get; init; } = new();

    public TorrentCandidateParseResult Parsed { get; init; } = new();
}
