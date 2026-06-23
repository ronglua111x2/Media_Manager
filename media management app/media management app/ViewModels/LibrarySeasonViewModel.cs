using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.ViewModels;

public partial class LibrarySeasonViewModel : ObservableObject
{
    private readonly Action<long, int, SeasonManagementMode> _managementModeChanged;

    public LibrarySeasonViewModel(
        long showId,
        int seasonNumber,
        IEnumerable<LibraryEpisodeRowViewModel> episodes,
        Action<long, int, SeasonManagementMode> managementModeChanged,
        TrackedSeason? seasonRecord)
    {
        _managementModeChanged = managementModeChanged;
        ShowId = showId;
        SeasonNumber = seasonNumber;
        Episodes = new ObservableCollection<LibraryEpisodeRowViewModel>(episodes);
        isPackMode = seasonNumber != AppConstants.SpecialsSeasonNumber &&
                     seasonRecord?.ManagementMode == SeasonManagementMode.Pack;
        selectedPackOwnerSeasonNumber = seasonRecord?.SelectedPackOwnerSeasonNumber;
        selectedPackName = seasonRecord?.SelectedPackCandidateName ?? string.Empty;
        selectedPackCoveredSeasons = seasonRecord?.SelectedPackCoveredSeasons ?? string.Empty;
        PackContentWarning = BuildPackContentWarning(seasonRecord);
        packTorrentHash = seasonRecord?.PackTorrentHash ?? string.Empty;
        packTorrentProgress = seasonRecord?.PackTorrentProgress ?? 0;
        isHidden = seasonRecord?.IsHidden ?? false;
        SyncEpisodePackMode();
    }

    public long ShowId { get; }

    public int SeasonNumber { get; }

    public string Header => SeasonNumber == AppConstants.SpecialsSeasonNumber
        ? $"Extras/Specials/OVAs | {SeasonStats}"
        : $"Season {SeasonNumber:00} | {SeasonStats}";

    public string SeasonStats =>
        $"{AvailableEpisodes}/{TrackedEpisodeCount} available | {MissingEpisodes} missing";

    public int TrackedEpisodeCount => Episodes.Count(episode => episode.IsTrackedEpisode);

    public int TotalEpisodes => TrackedEpisodeCount;

    public int AvailableEpisodes => Episodes.Count(episode => episode.IsTrackedEpisode && episode.IsAvailable);

    public int MissingEpisodes => Episodes.Count(episode => episode.IsTrackedEpisode && !episode.IsAvailable);

    public ObservableCollection<LibraryEpisodeRowViewModel> Episodes { get; }

    public bool IsEpisodeWorkflowEnabled => !IsPackMode;

    public bool HasSavedPack => !string.IsNullOrWhiteSpace(SelectedPackName);

    public bool IsPackOwner => HasSavedPack && SelectedPackOwnerSeasonNumber == SeasonNumber;

    public bool IsCoveredByAnotherPack =>
        HasSavedPack && SelectedPackOwnerSeasonNumber is not null && SelectedPackOwnerSeasonNumber != SeasonNumber;

    public string PackOwnerDisplay =>
        SelectedPackOwnerSeasonNumber is null ? string.Empty : $"S{SelectedPackOwnerSeasonNumber:00}";

    public string ManagementModeLabel => IsPackMode ? "Pack" : "Episodes";

    public string HideSeasonIconKind => IsHidden ? "EyeOff" : "Eye";

    public string HideSeasonToolTip => IsHidden ? "Show season again" : "Hide season";

    public string PackModeBannerTitle => IsCoveredByAnotherPack
        ? $"Managed by pack selected on {PackOwnerDisplay}: {SelectedPackName}"
        : IsPackMode
            ? "Pack mode: add the whole season pack to cart."
            : "Episode mode: add individual episodes to cart.";

    public bool HasPackContentWarning => !string.IsNullOrWhiteSpace(PackContentWarning);

    public string PackLinkStatus => IsCoveredByAnotherPack
        ? $"Covered by {PackOwnerDisplay}"
        : IsPackLinked
            ? "Linked"
            : IsPackTorrentComplete
                ? "Ready to link"
                : HasPackTorrent
                    ? PackTorrentProgress > 0 ? "Downloading" : "Added"
                    : IsPackInCart ? "In cart" : IsPackMode ? "Pack mode" : "Episode mode";

    public bool CanAddPackToCart => IsPackMode && !IsCoveredByAnotherPack && !IsPackInCart && !HasPackTorrent;

    public bool HasPackTorrent => !string.IsNullOrWhiteSpace(PackTorrentHash);

    public bool IsPackTorrentComplete => HasPackTorrent && PackTorrentProgress >= 0.999;

    public bool IsGeminiLinkAvailable { get; set; }

    public bool CanRuleLinkPack =>
        IsPackMode && !IsCoveredByAnotherPack && HasPackTorrent && !IsPackLinked && IsPackTorrentComplete;

    public bool CanAiLinkPack => CanRuleLinkPack && IsGeminiLinkAvailable;

    public bool CanUnlinkPack => IsPackMode && !IsCoveredByAnotherPack && IsPackLinked;

    public bool ShowPackLinkButtons => CanRuleLinkPack;

    public bool ShowAiLinkButton => CanAiLinkPack;

    public bool ShowPackUnlinkButton => CanUnlinkPack;

    public string RuleLinkPackToolTip => "Link pack using rules only (no AI API call)";

    public string AiLinkPackToolTip => "Link pack with Gemini special/OVA mapping and review";

    public string UnlinkPackToolTip => "Remove generated library hardlinks for this season pack";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddPackToCart))]
    [NotifyPropertyChangedFor(nameof(PackLinkStatus))]
    private bool isPackInCart;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRuleLinkPack))]
    [NotifyPropertyChangedFor(nameof(CanAiLinkPack))]
    [NotifyPropertyChangedFor(nameof(CanUnlinkPack))]
    [NotifyPropertyChangedFor(nameof(ShowPackLinkButtons))]
    [NotifyPropertyChangedFor(nameof(ShowPackUnlinkButton))]
    [NotifyPropertyChangedFor(nameof(PackLinkStatus))]
    private bool isPackLinked;

    public bool CanTogglePackMode => !IsCoveredByAnotherPack && SeasonNumber != AppConstants.SpecialsSeasonNumber;

    [ObservableProperty]
    private bool isExpanded;

    [ObservableProperty]
    private bool isHidden;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ManagementModeLabel))]
    [NotifyPropertyChangedFor(nameof(IsEpisodeWorkflowEnabled))]
    [NotifyPropertyChangedFor(nameof(PackModeBannerTitle))]
    [NotifyPropertyChangedFor(nameof(PackLinkStatus))]
    [NotifyPropertyChangedFor(nameof(CanAddPackToCart))]
    [NotifyPropertyChangedFor(nameof(CanRuleLinkPack))]
    [NotifyPropertyChangedFor(nameof(CanAiLinkPack))]
    [NotifyPropertyChangedFor(nameof(CanUnlinkPack))]
    [NotifyPropertyChangedFor(nameof(ShowPackLinkButtons))]
    [NotifyPropertyChangedFor(nameof(ShowPackUnlinkButton))]
    [NotifyPropertyChangedFor(nameof(CanTogglePackMode))]
    private bool isPackMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPackOwner))]
    [NotifyPropertyChangedFor(nameof(IsCoveredByAnotherPack))]
    [NotifyPropertyChangedFor(nameof(PackOwnerDisplay))]
    [NotifyPropertyChangedFor(nameof(PackModeBannerTitle))]
    [NotifyPropertyChangedFor(nameof(PackLinkStatus))]
    [NotifyPropertyChangedFor(nameof(CanAddPackToCart))]
    [NotifyPropertyChangedFor(nameof(CanRuleLinkPack))]
    [NotifyPropertyChangedFor(nameof(CanAiLinkPack))]
    [NotifyPropertyChangedFor(nameof(CanUnlinkPack))]
    [NotifyPropertyChangedFor(nameof(ShowPackLinkButtons))]
    [NotifyPropertyChangedFor(nameof(ShowPackUnlinkButton))]
    [NotifyPropertyChangedFor(nameof(CanTogglePackMode))]
    private int? selectedPackOwnerSeasonNumber;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSavedPack))]
    [NotifyPropertyChangedFor(nameof(IsPackOwner))]
    [NotifyPropertyChangedFor(nameof(IsCoveredByAnotherPack))]
    [NotifyPropertyChangedFor(nameof(PackModeBannerTitle))]
    [NotifyPropertyChangedFor(nameof(CanAddPackToCart))]
    [NotifyPropertyChangedFor(nameof(CanRuleLinkPack))]
    [NotifyPropertyChangedFor(nameof(CanAiLinkPack))]
    [NotifyPropertyChangedFor(nameof(CanUnlinkPack))]
    [NotifyPropertyChangedFor(nameof(ShowPackUnlinkButton))]
    [NotifyPropertyChangedFor(nameof(CanTogglePackMode))]
    private string selectedPackName = string.Empty;

    [ObservableProperty]
    private string selectedPackCoveredSeasons = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPackContentWarning))]
    private string packContentWarning = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRuleLinkPack))]
    [NotifyPropertyChangedFor(nameof(CanAiLinkPack))]
    [NotifyPropertyChangedFor(nameof(CanUnlinkPack))]
    [NotifyPropertyChangedFor(nameof(ShowPackLinkButtons))]
    [NotifyPropertyChangedFor(nameof(ShowPackUnlinkButton))]
    [NotifyPropertyChangedFor(nameof(HasPackTorrent))]
    [NotifyPropertyChangedFor(nameof(IsPackTorrentComplete))]
    [NotifyPropertyChangedFor(nameof(CanAddPackToCart))]
    [NotifyPropertyChangedFor(nameof(PackLinkStatus))]
    private string packTorrentHash = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPackTorrentComplete))]
    [NotifyPropertyChangedFor(nameof(CanRuleLinkPack))]
    [NotifyPropertyChangedFor(nameof(CanAiLinkPack))]
    [NotifyPropertyChangedFor(nameof(ShowPackLinkButtons))]
    [NotifyPropertyChangedFor(nameof(PackLinkStatus))]
    private double packTorrentProgress;

    partial void OnIsPackModeChanged(bool value)
    {
        SyncEpisodePackMode();
        _managementModeChanged(ShowId, SeasonNumber, value ? SeasonManagementMode.Pack : SeasonManagementMode.Episode);
        NotifySeasonChanged();
    }

    public void NotifySeasonChanged()
    {
        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(SeasonStats));
        OnPropertyChanged(nameof(AvailableEpisodes));
        OnPropertyChanged(nameof(MissingEpisodes));
    }

    private void SyncEpisodePackMode()
    {
        foreach (var episode in Episodes)
        {
            episode.IsSeasonPackMode = IsPackMode;
        }
    }

    private static string BuildPackContentWarning(TrackedSeason? seasonRecord)
    {
        if (seasonRecord is null || string.IsNullOrWhiteSpace(seasonRecord.SelectedPackContentProfile))
        {
            return string.Empty;
        }

        return PackContentProfile.Deserialize(seasonRecord.SelectedPackContentProfile)?.BuildWarningText() ?? string.Empty;
    }
}
