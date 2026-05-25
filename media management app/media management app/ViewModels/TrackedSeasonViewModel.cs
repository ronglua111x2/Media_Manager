using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.ViewModels;

public partial class TrackedSeasonViewModel : ObservableObject
{
    private readonly long _showId;
    private readonly Action<long, int, string?> _downloadFolderChanged;
    private readonly Action<long, int, SeasonManagementMode> _managementModeChanged;

    public TrackedSeasonViewModel(
        long showId,
        int seasonNumber,
        IEnumerable<TrackedEpisodeRowViewModel> episodes,
        IReadOnlyList<string> downloadFolderOptions,
        string? selectedDownloadFolder,
        Action<long, int, string?> downloadFolderChanged,
        Action<long, int, SeasonManagementMode> managementModeChanged,
        TrackedSeason? seasonRecord)
    {
        _showId = showId;
        _downloadFolderChanged = downloadFolderChanged;
        _managementModeChanged = managementModeChanged;
        SeasonNumber = seasonNumber;
        Episodes = new ObservableCollection<TrackedEpisodeRowViewModel>(episodes);
        SelectedEpisodes = [];
        PackCandidates = [];
        DownloadFolderOptions = new ObservableCollection<string>(downloadFolderOptions);
        selectedSeasonDownloadFolder = selectedDownloadFolder ?? string.Empty;
        isPackMode = seasonRecord?.ManagementMode == SeasonManagementMode.Pack;
        selectedPackOwnerSeasonNumber = seasonRecord?.SelectedPackOwnerSeasonNumber;
        selectedPackName = seasonRecord?.SelectedPackCandidateName ?? string.Empty;
        selectedPackCoveredSeasons = seasonRecord?.SelectedPackCoveredSeasons ?? string.Empty;
        packTorrentState = seasonRecord?.PackTorrentState ?? string.Empty;
        packTorrentStatus = BuildTorrentStatus(seasonRecord?.PackTorrentState, seasonRecord?.PackTorrentProgress ?? 0);
        if (CreateSavedPackCandidate(showId, seasonRecord) is { } savedPack)
        {
            PackCandidates.Add(savedPack);
            selectedPackCandidate = savedPack;
        }
        foreach (var episode in Episodes)
        {
            episode.DownloadFolder = SelectedSeasonDownloadFolder;
            episode.IsPackMode = IsPackMode;
            episode.PropertyChanged += Episode_PropertyChanged;
        }
    }

    public int SeasonNumber { get; }

    public long ShowId => _showId;

    public string Header => $"Season {SeasonNumber:00} | {SeasonStats}";

    public string SeasonStats =>
        $"{AvailableEpisodes}/{TotalEpisodes} available | {MissingEpisodes} missing | {WantedEpisodes} wanted | {CandidateEpisodes} candidates | {AddedEpisodes} added | selected {SelectedCandidateSizeDisplay}";

    public int TotalEpisodes => Episodes.Count;

    public int AvailableEpisodes => Episodes.Count(episode => !episode.IsMissing);

    public int MissingEpisodes => Episodes.Count(episode => episode.IsMissing);

    public int WantedEpisodes => Episodes.Count(episode => episode.IsWanted);

    public int CandidateEpisodes => Episodes.Count(episode => episode.Candidates.Count > 0);

    public int AddedEpisodes => Episodes.Count(episode => episode.FetchStatus == Common.EpisodeFetchStatus.Added);

    public long SelectedCandidateBytes => Episodes.Sum(episode => episode.SelectedCandidateBytes);

    public string SelectedCandidateSizeDisplay => StorageStatusViewModel.FormatSize(SelectedCandidateBytes);

    public bool HasSavedPack => !string.IsNullOrWhiteSpace(SelectedPackName);

    public bool IsPackOwner => HasSavedPack && SelectedPackOwnerSeasonNumber == SeasonNumber;

    public bool IsCoveredByAnotherPack => HasSavedPack && SelectedPackOwnerSeasonNumber is not null && SelectedPackOwnerSeasonNumber != SeasonNumber;

    public bool IsPackCandidateEditable => !IsCoveredByAnotherPack;

    public string PackOwnerDisplay => SelectedPackOwnerSeasonNumber is null ? string.Empty : $"S{SelectedPackOwnerSeasonNumber:00}";

    public long SelectedPackBytes => IsPackOwner ? SelectedPackCandidate?.FileSize ?? 0 : 0;

    public string SelectedPackSizeDisplay => StorageStatusViewModel.FormatSize(SelectedPackBytes);

    public string UniquePackSizeDisplay => IsCoveredByAnotherPack
        ? $"Covered by {PackOwnerDisplay}"
        : SelectedPackSizeDisplay;

    public string PackStorageSummary
    {
        get
        {
            if (IsCoveredByAnotherPack)
            {
                return $"Uses {PackOwnerDisplay}";
            }

            return !IsPackOwner || SelectedPackCandidate is null
                ? "No pack selected"
                : $"{SelectedPackSizeDisplay} to {SelectedDownloadFolderDisplay}";
        }
    }

    public bool IsEpisodeWorkflowEnabled => !IsPackMode;

    public string PackModeBannerTitle => HasSavedPack
        ? $"Managed by pack selected on {PackOwnerDisplay}: {SelectedPackName}"
        : "Pack mode";

    public string PackLinkSummary => HasSavedPack
        ? $"Linked {PackLinkedEpisodeCount}/{PackTotalEpisodeCount} episodes from pack"
        : string.Empty;

    public string PackLinkStatus
    {
        get
        {
            if (IsCoveredByAnotherPack)
            {
                return $"Covered by {PackOwnerDisplay}";
            }

            if (!HasSavedPack)
            {
                return "Not linked";
            }

            if (string.IsNullOrWhiteSpace(PackTorrentState) ||
                string.Equals(PackTorrentState, "Removed from qBittorrent", StringComparison.OrdinalIgnoreCase))
            {
                return "Torrent missing";
            }

            if (!string.Equals(PackTorrentState, "Downloaded", StringComparison.OrdinalIgnoreCase))
            {
                return "Torrent not complete";
            }

            if (PackLinkedEpisodeCount == 0 || PackTotalEpisodeCount == 0)
            {
                return "Not linked";
            }

            return PackLinkedEpisodeCount >= PackTotalEpisodeCount
                ? $"Linked {PackLinkedEpisodeCount}/{PackTotalEpisodeCount}"
                : $"Partial {PackLinkedEpisodeCount}/{PackTotalEpisodeCount}";
        }
    }

    public ObservableCollection<TrackedEpisodeRowViewModel> Episodes { get; }

    public ObservableCollection<TrackedEpisodeRowViewModel> SelectedEpisodes { get; }

    public ObservableCollection<SeasonPackCandidate> PackCandidates { get; }

    public ObservableCollection<string> DownloadFolderOptions { get; }

    [ObservableProperty]
    private bool isExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDownloadFolderDisplay))]
    [NotifyPropertyChangedFor(nameof(PackStorageSummary))]
    private string selectedSeasonDownloadFolder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ManagementModeLabel))]
    [NotifyPropertyChangedFor(nameof(IsEpisodeWorkflowEnabled))]
    [NotifyPropertyChangedFor(nameof(HasPackWarning))]
    [NotifyPropertyChangedFor(nameof(IsPackOwner))]
    [NotifyPropertyChangedFor(nameof(IsCoveredByAnotherPack))]
    [NotifyPropertyChangedFor(nameof(IsPackCandidateEditable))]
    private bool isPackMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedPackSummary))]
    [NotifyPropertyChangedFor(nameof(HasPackWarning))]
    [NotifyPropertyChangedFor(nameof(HasSavedPack))]
    [NotifyPropertyChangedFor(nameof(IsPackOwner))]
    [NotifyPropertyChangedFor(nameof(IsCoveredByAnotherPack))]
    [NotifyPropertyChangedFor(nameof(IsPackCandidateEditable))]
    [NotifyPropertyChangedFor(nameof(SelectedPackBytes))]
    [NotifyPropertyChangedFor(nameof(SelectedPackSizeDisplay))]
    [NotifyPropertyChangedFor(nameof(UniquePackSizeDisplay))]
    [NotifyPropertyChangedFor(nameof(PackStorageSummary))]
    [NotifyPropertyChangedFor(nameof(PackModeBannerTitle))]
    [NotifyPropertyChangedFor(nameof(PackLinkSummary))]
    [NotifyPropertyChangedFor(nameof(PackLinkStatus))]
    private SeasonPackCandidate? selectedPackCandidate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedPackSummary))]
    [NotifyPropertyChangedFor(nameof(HasPackWarning))]
    [NotifyPropertyChangedFor(nameof(HasSavedPack))]
    [NotifyPropertyChangedFor(nameof(IsPackOwner))]
    [NotifyPropertyChangedFor(nameof(IsCoveredByAnotherPack))]
    [NotifyPropertyChangedFor(nameof(IsPackCandidateEditable))]
    [NotifyPropertyChangedFor(nameof(SelectedPackBytes))]
    [NotifyPropertyChangedFor(nameof(SelectedPackSizeDisplay))]
    [NotifyPropertyChangedFor(nameof(UniquePackSizeDisplay))]
    [NotifyPropertyChangedFor(nameof(PackStorageSummary))]
    [NotifyPropertyChangedFor(nameof(PackModeBannerTitle))]
    [NotifyPropertyChangedFor(nameof(PackLinkSummary))]
    [NotifyPropertyChangedFor(nameof(PackLinkStatus))]
    private string selectedPackName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedPackSummary))]
    [NotifyPropertyChangedFor(nameof(HasPackWarning))]
    [NotifyPropertyChangedFor(nameof(PackModeBannerTitle))]
    private string selectedPackCoveredSeasons = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedPackSummary))]
    [NotifyPropertyChangedFor(nameof(HasPackWarning))]
    [NotifyPropertyChangedFor(nameof(IsPackOwner))]
    [NotifyPropertyChangedFor(nameof(IsCoveredByAnotherPack))]
    [NotifyPropertyChangedFor(nameof(IsPackCandidateEditable))]
    [NotifyPropertyChangedFor(nameof(PackOwnerDisplay))]
    [NotifyPropertyChangedFor(nameof(SelectedPackBytes))]
    [NotifyPropertyChangedFor(nameof(SelectedPackSizeDisplay))]
    [NotifyPropertyChangedFor(nameof(UniquePackSizeDisplay))]
    [NotifyPropertyChangedFor(nameof(PackStorageSummary))]
    [NotifyPropertyChangedFor(nameof(PackModeBannerTitle))]
    [NotifyPropertyChangedFor(nameof(PackLinkStatus))]
    private int? selectedPackOwnerSeasonNumber;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PackLinkStatus))]
    private string packTorrentState = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PackLinkStatus))]
    private string packTorrentStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PackLinkSummary))]
    [NotifyPropertyChangedFor(nameof(PackLinkStatus))]
    private int packLinkedEpisodeCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PackLinkSummary))]
    [NotifyPropertyChangedFor(nameof(PackLinkStatus))]
    private int packTotalEpisodeCount;

    public string ManagementModeLabel => IsPackMode ? "Pack" : "Episodes";

    public bool HasPackWarning => !string.IsNullOrWhiteSpace(SelectedPackName);

    public string SelectedPackSummary
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SelectedPackName))
            {
                return string.Empty;
            }

            var owner = SelectedPackOwnerSeasonNumber is null ? string.Empty : $"S{SelectedPackOwnerSeasonNumber:00}";
            return SelectedPackOwnerSeasonNumber == SeasonNumber
                ? $"Selected pack covers {FormatCoveredSeasons(SelectedPackCoveredSeasons)}"
                : $"Managed by pack selected on {owner}: {SelectedPackName}";
        }
    }

    public string SelectedDownloadFolderDisplay => string.IsNullOrWhiteSpace(SelectedSeasonDownloadFolder)
        ? "Default download folder"
        : SelectedSeasonDownloadFolder;

    partial void OnSelectedSeasonDownloadFolderChanged(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value;
        _downloadFolderChanged(_showId, SeasonNumber, normalized);
        foreach (var episode in Episodes)
        {
            episode.DownloadFolder = value;
        }

        NotifyStatsChanged();
    }

    partial void OnIsPackModeChanged(bool value)
    {
        _managementModeChanged(_showId, SeasonNumber, value ? SeasonManagementMode.Pack : SeasonManagementMode.Episode);
        foreach (var episode in Episodes)
        {
            episode.IsPackMode = value;
        }

        OnPropertyChanged(nameof(IsEpisodeWorkflowEnabled));
    }

    partial void OnSelectedPackCandidateChanged(SeasonPackCandidate? value)
    {
        NotifyStatsChanged();
    }

    public void ReplacePackCandidates(IReadOnlyList<SeasonPackCandidate> candidates)
    {
        var selectedUrl = SelectedPackCandidate?.FileUrl;
        PackCandidates.Clear();
        foreach (var candidate in candidates)
        {
            PackCandidates.Add(candidate);
        }

        SelectedPackCandidate = PackCandidates.FirstOrDefault(candidate =>
                                    string.Equals(candidate.FileUrl, selectedUrl, StringComparison.OrdinalIgnoreCase)) ??
                                PackCandidates.FirstOrDefault();
    }

    public void ApplySavedPack(SeasonPackCandidate candidate, int ownerSeasonNumber)
    {
        SelectedPackName = candidate.FileName;
        SelectedPackCoveredSeasons = string.Join(",", candidate.CoveredSeasons);
        SelectedPackOwnerSeasonNumber = ownerSeasonNumber;
        if (!PackCandidates.Any(item => string.Equals(item.FileUrl, candidate.FileUrl, StringComparison.OrdinalIgnoreCase)))
        {
            PackCandidates.Insert(0, candidate);
        }

        SelectedPackCandidate = PackCandidates.FirstOrDefault(item => string.Equals(item.FileUrl, candidate.FileUrl, StringComparison.OrdinalIgnoreCase));
    }

    public void ClearSavedPack()
    {
        SelectedPackName = string.Empty;
        SelectedPackCoveredSeasons = string.Empty;
        SelectedPackOwnerSeasonNumber = null;
        PackTorrentState = string.Empty;
        PackTorrentStatus = string.Empty;
    }

    [RelayCommand]
    private void MarkMissingWanted()
    {
        if (IsPackMode)
        {
            return;
        }

        foreach (var episode in Episodes.Where(episode => episode.IsMissing))
        {
            episode.IsWanted = true;
        }
    }

    [RelayCommand]
    private void ClearWanted()
    {
        if (IsPackMode)
        {
            return;
        }

        foreach (var episode in Episodes)
        {
            episode.IsWanted = false;
        }
    }

    [RelayCommand]
    private void MarkSelectedWanted()
    {
        if (IsPackMode)
        {
            return;
        }

        foreach (var episode in SelectedEpisodes.Where(episode => episode.IsMissing))
        {
            episode.IsWanted = true;
        }
    }

    private void Episode_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TrackedEpisodeRowViewModel.IsWanted) or
            nameof(TrackedEpisodeRowViewModel.FetchStatus) or
            nameof(TrackedEpisodeRowViewModel.SelectedCandidate))
        {
            NotifyStatsChanged();
        }
    }

    public void NotifyStatsChanged()
    {
        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(SeasonStats));
        OnPropertyChanged(nameof(AvailableEpisodes));
        OnPropertyChanged(nameof(MissingEpisodes));
        OnPropertyChanged(nameof(WantedEpisodes));
        OnPropertyChanged(nameof(CandidateEpisodes));
        OnPropertyChanged(nameof(AddedEpisodes));
        OnPropertyChanged(nameof(SelectedCandidateBytes));
        OnPropertyChanged(nameof(SelectedCandidateSizeDisplay));
        OnPropertyChanged(nameof(SelectedPackBytes));
        OnPropertyChanged(nameof(SelectedPackSizeDisplay));
        OnPropertyChanged(nameof(UniquePackSizeDisplay));
        OnPropertyChanged(nameof(PackStorageSummary));
        OnPropertyChanged(nameof(HasSavedPack));
        OnPropertyChanged(nameof(IsPackOwner));
        OnPropertyChanged(nameof(IsCoveredByAnotherPack));
        OnPropertyChanged(nameof(IsPackCandidateEditable));
        OnPropertyChanged(nameof(PackOwnerDisplay));
        OnPropertyChanged(nameof(IsEpisodeWorkflowEnabled));
        OnPropertyChanged(nameof(PackModeBannerTitle));
        OnPropertyChanged(nameof(PackLinkSummary));
        OnPropertyChanged(nameof(PackLinkStatus));
    }

    private static SeasonPackCandidate? CreateSavedPackCandidate(long showId, TrackedSeason? season)
    {
        if (season is null ||
            string.IsNullOrWhiteSpace(season.SelectedPackCandidateUrl) ||
            string.IsNullOrWhiteSpace(season.SelectedPackCandidateName))
        {
            return null;
        }

        return new SeasonPackCandidate
        {
            ShowId = showId,
            OwnerSeasonNumber = season.SelectedPackOwnerSeasonNumber ?? season.SeasonNumber,
            FileName = season.SelectedPackCandidateName,
            FileUrl = season.SelectedPackCandidateUrl,
            PluginName = season.SelectedPackCandidatePlugin ?? string.Empty,
            FileSize = season.SelectedPackCandidateFileSize,
            Seeders = season.SelectedPackCandidateSeeders,
            QualityLabel = season.SelectedPackCandidateQuality ?? string.Empty,
            AudioCodecLabel = season.SelectedPackCandidateAudioCodec ?? string.Empty,
            CoveredSeasons = ParseCoveredSeasons(season.SelectedPackCoveredSeasons)
        };
    }

    private static IReadOnlyList<int> ParseCoveredSeasons(string? value)
    {
        return (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => int.TryParse(item, out var season) ? season : 0)
            .Where(season => season > 0)
            .Distinct()
            .Order()
            .ToList();
    }

    private static string FormatCoveredSeasons(string value)
    {
        var seasons = ParseCoveredSeasons(value);
        return seasons.Count == 0 ? string.Empty : string.Join(", ", seasons.Select(season => $"S{season:00}"));
    }

    private static string BuildTorrentStatus(string? state, double progress)
    {
        return string.IsNullOrWhiteSpace(state) ? string.Empty : $"{state} ({progress:P0})";
    }
}
