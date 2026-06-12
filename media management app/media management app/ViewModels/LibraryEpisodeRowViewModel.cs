using CommunityToolkit.Mvvm.ComponentModel;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.ViewModels;

public sealed partial class LibraryEpisodeRowViewModel : ObservableObject
{
    public LibraryEpisodeRowViewModel(TrackedEpisode episode)
    {
        Id = episode.Id;
        ShowId = episode.ShowId;
        SeasonNumber = episode.SeasonNumber;
        EpisodeNumber = episode.EpisodeNumber;
        Title = episode.Title;
        AirDateDisplay = episode.AirDateDisplay;
        Availability = episode.Availability;
        TorrentHash = episode.TorrentHash ?? string.Empty;
        TorrentState = episode.TorrentState ?? string.Empty;
        TorrentProgress = episode.TorrentProgress;
    }

    public long Id { get; }

    public long ShowId { get; }

    public int SeasonNumber { get; }

    public int EpisodeNumber { get; }

    public string EpisodeCode => $"S{SeasonNumber:00}E{EpisodeNumber:00}";

    public string Title { get; }

    public string AirDateDisplay { get; }

    public EpisodeAvailability Availability { get; }

    public bool IsAvailable => Availability == EpisodeAvailability.Available;

    public string TorrentHash { get; }

    public string TorrentState { get; }

    public double TorrentProgress { get; }

    public bool HasTorrent => !string.IsNullOrWhiteSpace(TorrentHash);

    public bool IsTorrentComplete => HasTorrent && TorrentProgress >= 0.999;

    public bool CanAddToCart => !IsSeasonPackMode && !IsAvailable && !IsInCart && !HasTorrent;

    public bool IsLinked => !string.Equals(LibraryLinkStatus, "Not linked", StringComparison.OrdinalIgnoreCase);

    public bool CanLink => !IsSeasonPackMode && (HasTorrent || IsLinked);

    public string LinkActionLabel => IsLinked ? "Unlink" : "Link";

    public string LinkActionIconKind => IsLinked ? "Unlink" : "Link";

    public string LinkActionToolTip => IsLinked
        ? "Remove generated library hardlink for this episode"
        : "Create library hardlink for this episode";

    public string PrimaryStatusLabel => AvailabilityLabel;

    public string CompactLinkStatus => LibraryLinkStatus switch
    {
        "Linked by episode torrent" => "Episode link",
        var status when status.StartsWith("Linked by pack", StringComparison.OrdinalIgnoreCase) => status,
        "Linked" => "Linked",
        _ => "Not linked"
    };

    public bool ShowWorkflowStatus => !string.IsNullOrWhiteSpace(WorkflowStatusLabel) &&
                                      !string.Equals(WorkflowStatusLabel, "Linked", StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddToCart))]
    [NotifyPropertyChangedFor(nameof(WorkflowStatusLabel))]
    [NotifyPropertyChangedFor(nameof(ShowWorkflowStatus))]
    private bool isInCart;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddToCart))]
    [NotifyPropertyChangedFor(nameof(CanLink))]
    private bool isSeasonPackMode;

    public string AvailabilityLabel => IsAvailable ? "Available" : "Missing";

    public string WorkflowStatusLabel
    {
        get
        {
            if (IsLinked)
            {
                return "Linked";
            }

            if (IsTorrentComplete)
            {
                return "Ready to link";
            }

            if (HasTorrent)
            {
                return TorrentProgress > 0 ? "Downloading" : "Added";
            }

            return IsInCart ? "In cart" : string.Empty;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLinked))]
    [NotifyPropertyChangedFor(nameof(CanLink))]
    [NotifyPropertyChangedFor(nameof(LinkActionLabel))]
    [NotifyPropertyChangedFor(nameof(LinkActionIconKind))]
    [NotifyPropertyChangedFor(nameof(LinkActionToolTip))]
    [NotifyPropertyChangedFor(nameof(WorkflowStatusLabel))]
    [NotifyPropertyChangedFor(nameof(CompactLinkStatus))]
    [NotifyPropertyChangedFor(nameof(ShowWorkflowStatus))]
    private string libraryLinkStatus = "Not linked";
}
