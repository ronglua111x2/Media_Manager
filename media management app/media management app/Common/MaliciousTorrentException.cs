namespace media_management_app.Common;

/// <summary>
/// Thrown when an added torrent fails malware content validation and was verified deleted
/// and blacklisted. Outer add loops use this to try the next candidate rank.
/// Unverified deletion throws <see cref="TorrentCleanupFailedException"/> instead.
/// </summary>
public sealed class MaliciousTorrentException : Exception
{
    public MaliciousTorrentException(string message)
        : base(message)
    {
    }
}
