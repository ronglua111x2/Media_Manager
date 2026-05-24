using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace media_management_app.ViewModels;

public partial class TrackedSeasonViewModel : ObservableObject
{
    private readonly long _showId;
    private readonly Action<long, int, string?> _downloadFolderChanged;

    public TrackedSeasonViewModel(
        long showId,
        int seasonNumber,
        IEnumerable<TrackedEpisodeRowViewModel> episodes,
        IReadOnlyList<string> downloadFolderOptions,
        string? selectedDownloadFolder,
        Action<long, int, string?> downloadFolderChanged)
    {
        _showId = showId;
        _downloadFolderChanged = downloadFolderChanged;
        SeasonNumber = seasonNumber;
        Episodes = new ObservableCollection<TrackedEpisodeRowViewModel>(episodes);
        SelectedEpisodes = [];
        DownloadFolderOptions = new ObservableCollection<string>(downloadFolderOptions);
        selectedSeasonDownloadFolder = selectedDownloadFolder ?? string.Empty;
        foreach (var episode in Episodes)
        {
            episode.DownloadFolder = SelectedSeasonDownloadFolder;
            episode.PropertyChanged += Episode_PropertyChanged;
        }
    }

    public int SeasonNumber { get; }

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

    public ObservableCollection<TrackedEpisodeRowViewModel> Episodes { get; }

    public ObservableCollection<TrackedEpisodeRowViewModel> SelectedEpisodes { get; }

    public ObservableCollection<string> DownloadFolderOptions { get; }

    [ObservableProperty]
    private bool isExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDownloadFolderDisplay))]
    private string selectedSeasonDownloadFolder;

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
    }

    [RelayCommand]
    private void MarkMissingWanted()
    {
        foreach (var episode in Episodes.Where(episode => episode.IsMissing))
        {
            episode.IsWanted = true;
        }
    }

    [RelayCommand]
    private void ClearWanted()
    {
        foreach (var episode in Episodes)
        {
            episode.IsWanted = false;
        }
    }

    [RelayCommand]
    private void MarkSelectedWanted()
    {
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
    }
}
