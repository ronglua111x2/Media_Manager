using CommunityToolkit.Mvvm.ComponentModel;
using media_management_app.Common;

namespace media_management_app.ViewModels;

public sealed partial class TorrentOrderViewModel : ObservableObject
{
    public long Id { get; init; }

    public MediaKind TargetKind { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public TorrentOrderStatus Status { get; init; } = TorrentOrderStatus.Draft;

    public string StatusDetail { get; init; } = string.Empty;

    public string StatusLabel => Status switch
    {
        TorrentOrderStatus.Draft => "Draft",
        TorrentOrderStatus.Searching => "Searching",
        TorrentOrderStatus.CandidatesFound => "Candidates found",
        TorrentOrderStatus.NoCandidates => "No candidates",
        TorrentOrderStatus.Approved => "Approved",
        TorrentOrderStatus.AddedToClient => "Added to qBittorrent",
        TorrentOrderStatus.Downloading => "Downloading",
        TorrentOrderStatus.Completed => "Completed",
        TorrentOrderStatus.Failed => "Failed",
        TorrentOrderStatus.Canceled => "Canceled",
        _ => Status.ToString()
    };

    public string TargetLabel => TargetKind == MediaKind.Movie ? "Movie" : "Episode/Pack";

    public string DetailText => string.IsNullOrWhiteSpace(StatusDetail) ? Summary : StatusDetail;
}
