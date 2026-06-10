using media_management_app.Common;

namespace media_management_app.Models;

public sealed class TorrentCartOrder
{
    public long Id { get; set; }

    public MediaKind TargetKind { get; set; }

    public long MediaId { get; set; }

    public long? EpisodeId { get; set; }

    public int? SeasonNumber { get; set; }

    public int? EpisodeNumber { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public TorrentOrderStatus Status { get; set; } = TorrentOrderStatus.Draft;

    public string StatusDetail { get; set; } = string.Empty;
}
