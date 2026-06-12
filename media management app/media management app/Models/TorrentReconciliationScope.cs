using media_management_app.Common;

namespace media_management_app.Models;

public sealed record TorrentReconciliationScope(MediaKind? MediaKind, long? MediaId)
{
    public static TorrentReconciliationScope All { get; } = new(null, null);

    public static TorrentReconciliationScope ForMedia(MediaKind mediaKind, long mediaId) => new(mediaKind, mediaId);

    public bool Includes(MediaKind mediaKind, long mediaId)
    {
        return MediaKind is null ||
               (MediaKind == mediaKind && MediaId == mediaId);
    }
}
