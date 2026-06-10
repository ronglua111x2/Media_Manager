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
        isPackMode = seasonRecord?.ManagementMode == SeasonManagementMode.Pack;
        selectedPackOwnerSeasonNumber = seasonRecord?.SelectedPackOwnerSeasonNumber;
        selectedPackName = seasonRecord?.SelectedPackCandidateName ?? string.Empty;
        selectedPackCoveredSeasons = seasonRecord?.SelectedPackCoveredSeasons ?? string.Empty;
    }

    public long ShowId { get; }

    public int SeasonNumber { get; }

    public string Header => $"Season {SeasonNumber:00} | {SeasonStats}";

    public string SeasonStats =>
        $"{AvailableEpisodes}/{TotalEpisodes} available | {MissingEpisodes} missing";

    public int TotalEpisodes => Episodes.Count;

    public int AvailableEpisodes => Episodes.Count(episode => episode.IsAvailable);

    public int MissingEpisodes => Episodes.Count(episode => !episode.IsAvailable);

    public ObservableCollection<LibraryEpisodeRowViewModel> Episodes { get; }

    public bool IsEpisodeWorkflowEnabled => !IsPackMode;

    public bool HasSavedPack => !string.IsNullOrWhiteSpace(SelectedPackName);

    public bool IsPackOwner => HasSavedPack && SelectedPackOwnerSeasonNumber == SeasonNumber;

    public bool IsCoveredByAnotherPack =>
        HasSavedPack && SelectedPackOwnerSeasonNumber is not null && SelectedPackOwnerSeasonNumber != SeasonNumber;

    public string PackOwnerDisplay =>
        SelectedPackOwnerSeasonNumber is null ? string.Empty : $"S{SelectedPackOwnerSeasonNumber:00}";

    public string ManagementModeLabel => IsPackMode ? "Pack" : "Episodes";

    public string PackModeBannerTitle => IsCoveredByAnotherPack
        ? $"Managed by pack selected on {PackOwnerDisplay}: {SelectedPackName}"
        : IsPackMode
            ? "Pack mode: add the whole season pack to cart."
            : "Episode mode: add individual episodes to cart.";

    public string PackLinkStatus => IsCoveredByAnotherPack
        ? $"Covered by {PackOwnerDisplay}"
        : IsPackMode ? "Pack mode" : "Episode mode";

    public bool CanAddPackToCart => IsPackMode && !IsCoveredByAnotherPack && !IsPackInCart;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddPackToCart))]
    private bool isPackInCart;

    public bool CanTogglePackMode => !IsCoveredByAnotherPack;

    [ObservableProperty]
    private bool isExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ManagementModeLabel))]
    [NotifyPropertyChangedFor(nameof(IsEpisodeWorkflowEnabled))]
    [NotifyPropertyChangedFor(nameof(PackModeBannerTitle))]
    [NotifyPropertyChangedFor(nameof(PackLinkStatus))]
    [NotifyPropertyChangedFor(nameof(CanAddPackToCart))]
    [NotifyPropertyChangedFor(nameof(CanTogglePackMode))]
    private bool isPackMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPackOwner))]
    [NotifyPropertyChangedFor(nameof(IsCoveredByAnotherPack))]
    [NotifyPropertyChangedFor(nameof(PackOwnerDisplay))]
    [NotifyPropertyChangedFor(nameof(PackModeBannerTitle))]
    [NotifyPropertyChangedFor(nameof(PackLinkStatus))]
    [NotifyPropertyChangedFor(nameof(CanAddPackToCart))]
    [NotifyPropertyChangedFor(nameof(CanTogglePackMode))]
    private int? selectedPackOwnerSeasonNumber;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSavedPack))]
    [NotifyPropertyChangedFor(nameof(IsPackOwner))]
    [NotifyPropertyChangedFor(nameof(IsCoveredByAnotherPack))]
    [NotifyPropertyChangedFor(nameof(PackModeBannerTitle))]
    [NotifyPropertyChangedFor(nameof(CanAddPackToCart))]
    [NotifyPropertyChangedFor(nameof(CanTogglePackMode))]
    private string selectedPackName = string.Empty;

    [ObservableProperty]
    private string selectedPackCoveredSeasons = string.Empty;

    partial void OnIsPackModeChanged(bool value)
    {
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
}
