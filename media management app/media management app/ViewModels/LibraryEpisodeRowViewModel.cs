using CommunityToolkit.Mvvm.ComponentModel;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.ViewModels;

public sealed partial class LibraryEpisodeRowViewModel : ObservableObject
{
    private LibraryEpisodeRowViewModel()
    {
    }

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
        IsTrackedEpisode = true;
    }

    public long Id { get; }

    public long ShowId { get; private init; }

    public int SeasonNumber { get; private init; }

    public int EpisodeNumber { get; }

    public long? SourceItemId { get; private init; }

    public bool IsTrackedEpisode { get; private init; }

    public bool IsOrphan { get; private init; }

    public bool IsOrphanSeparator { get; private init; }

    public string EpisodeCode => IsOrphanSeparator
        ? string.Empty
        : IsOrphan
            ? "-"
            : $"S{SeasonNumber:00}E{EpisodeNumber:00}";

    public string Title { get; private init; } = string.Empty;

    public string AirDateDisplay { get; private init; } = string.Empty;

    public EpisodeAvailability Availability { get; private init; }

    public bool IsAvailable => IsOrphan || Availability == EpisodeAvailability.Available;

    public string TorrentHash { get; private init; } = string.Empty;

    public string TorrentState { get; private init; } = string.Empty;

    public double TorrentProgress { get; private init; }

    public bool HasTorrent => !string.IsNullOrWhiteSpace(TorrentHash);

    public bool IsTorrentComplete => HasTorrent && TorrentProgress >= 0.999;

    public bool CanAddToCart => IsTrackedEpisode && !IsSeasonPackMode && !IsAvailable && !IsInCart && !HasTorrent;

    public bool IsLinked => !string.Equals(LibraryLinkStatus, "Not linked", StringComparison.OrdinalIgnoreCase);

    public bool CanLink => IsOrphan
        ? IsLinked
        : IsTrackedEpisode && !IsOrphanSeparator && !IsSeasonPackMode && (HasTorrent || IsLinked);

    public bool CanReset => IsTrackedEpisode && !IsOrphan && !IsOrphanSeparator && !IsSeasonPackMode &&
                            (HasTorrent || IsAvailable);

    public string LinkActionLabel => IsLinked ? "Unlink" : "Link";

    public string LinkActionIconKind => IsLinked ? "Unlink" : "Link";

    public string LinkActionToolTip => IsOrphan
        ? "Remove orphan library hardlink only — download file is not deleted"
        : IsLinked
            ? "Remove generated library hardlink for this episode"
            : "Create library hardlink for this episode";

    public string PrimaryStatusLabel => IsOrphanSeparator
        ? string.Empty
        : IsOrphan
            ? "Linked"
            : AvailabilityLabel;

    public string CompactLinkStatus => IsOrphanSeparator
        ? string.Empty
        : LibraryLinkStatus switch
        {
            "Linked by episode torrent" => "Episode link",
            var status when status.StartsWith("Linked by pack", StringComparison.OrdinalIgnoreCase) => status,
            "Linked" => "Linked",
            _ => "Not linked"
        };

    public bool ShowWorkflowStatus => !IsOrphan &&
                                      !IsOrphanSeparator &&
                                      !string.IsNullOrWhiteSpace(WorkflowStatusLabel) &&
                                      !string.Equals(WorkflowStatusLabel, "Linked", StringComparison.OrdinalIgnoreCase);

    public static LibraryEpisodeRowViewModel CreateOrphan(SourceItem item, long showId) =>
        new()
        {
            ShowId = showId,
            SeasonNumber = AppConstants.SpecialsSeasonNumber,
            Title = item.FileName,
            IsOrphan = true,
            SourceItemId = item.Id,
            LibraryLinkStatus = "Linked",
            Availability = EpisodeAvailability.Available
        };

    public static LibraryEpisodeRowViewModel CreateOrphanSeparator() =>
        new()
        {
            IsOrphanSeparator = true,
            Title = "Unmatched extras/specials/OVAs from pack. Unlink removes library hardlink only — your download file is not deleted."
        };

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
            if (IsOrphanSeparator)
            {
                return string.Empty;
            }

            if (IsOrphan || IsLinked)
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
    [NotifyPropertyChangedFor(nameof(IsAvailable))]
    [NotifyPropertyChangedFor(nameof(AvailabilityLabel))]
    [NotifyPropertyChangedFor(nameof(PrimaryStatusLabel))]
    private string libraryLinkStatus = "Not linked";
}
