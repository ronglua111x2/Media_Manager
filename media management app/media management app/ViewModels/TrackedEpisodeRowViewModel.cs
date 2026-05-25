using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.ViewModels;

public partial class TrackedEpisodeRowViewModel : ObservableObject
{
    private readonly Action<long, bool> _wantedChanged;

    public TrackedEpisodeRowViewModel(TrackedEpisode episode, Action<long, bool> wantedChanged)
    {
        _wantedChanged = wantedChanged;
        Id = episode.Id;
        ShowId = episode.ShowId;
        SeasonNumber = episode.SeasonNumber;
        EpisodeNumber = episode.EpisodeNumber;
        Title = episode.Title;
        AirDateDisplay = episode.AirDateDisplay;
        Availability = episode.Availability;
        isWanted = episode.IsWanted;
        torrentHash = episode.TorrentHash ?? string.Empty;
        torrentStatus = BuildTorrentStatus(episode.TorrentState, episode.TorrentProgress);
        Candidates = [];
        if (CreateSavedCandidate(episode) is { } savedCandidate)
        {
            Candidates.Add(new EpisodeCandidateViewModel(savedCandidate));
        }

        selectedCandidate = Candidates.FirstOrDefault();
        FetchStatus = EpisodeFetchStatus.NotFetched;
    }

    public long Id { get; }

    public long ShowId { get; }

    public int SeasonNumber { get; }

    public int EpisodeNumber { get; }

    public string EpisodeCode => $"S{SeasonNumber:00}E{EpisodeNumber:00}";

    public string Title { get; }

    public string AirDateDisplay { get; }

    public EpisodeAvailability Availability { get; }

    public bool IsMissing => Availability == EpisodeAvailability.Missing;

    public ObservableCollection<EpisodeCandidateViewModel> Candidates { get; }

    public long SelectedCandidateBytes => SelectedCandidate?.Candidate.FileSize ?? 0;

    public string SelectedCandidateSizeDisplay => StorageStatusViewModel.FormatSize(SelectedCandidateBytes);

    [ObservableProperty]
    private bool isWanted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FetchStatusDisplay))]
    private EpisodeFetchStatus fetchStatus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FetchStatusDisplay))]
    private bool isPackMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedCandidateBytes))]
    [NotifyPropertyChangedFor(nameof(SelectedCandidateSizeDisplay))]
    private EpisodeCandidateViewModel? selectedCandidate;

    [ObservableProperty]
    private string downloadFolder = string.Empty;

    [ObservableProperty]
    private string torrentHash = string.Empty;

    [ObservableProperty]
    private string torrentStatus = string.Empty;

    [ObservableProperty]
    private string libraryLinkStatus = "Not linked";

    public string FetchStatusDisplay => IsPackMode ? "Pack mode" : FetchStatus.ToString();

    partial void OnIsWantedChanged(bool value)
    {
        _wantedChanged(Id, value);
    }

    public void ReplaceCandidates(IReadOnlyList<EpisodeFetchCandidate> candidates)
    {
        var selectedUrl = SelectedCandidate?.Candidate.FileUrl;
        Candidates.Clear();
        foreach (var candidate in candidates)
        {
            Candidates.Add(new EpisodeCandidateViewModel(candidate));
        }

        SelectedCandidate = Candidates.FirstOrDefault(candidate =>
                                string.Equals(candidate.Candidate.FileUrl, selectedUrl, StringComparison.OrdinalIgnoreCase)) ??
                            Candidates.FirstOrDefault();
        FetchStatus = Candidates.Count == 0 ? EpisodeFetchStatus.NoCandidates : EpisodeFetchStatus.CandidatesFound;
    }

    public void MarkTorrentAdded(AddedTorrentResult torrent)
    {
        TorrentHash = torrent.Hash;
        TorrentStatus = BuildTorrentStatus(torrent.IsComplete ? "Downloaded" : torrent.State, torrent.Progress);
        FetchStatus = EpisodeFetchStatus.Added;
    }

    public void UpdateTorrentStatus(AddedTorrentResult torrent)
    {
        TorrentStatus = BuildTorrentStatus(torrent.IsComplete ? "Downloaded" : torrent.State, torrent.Progress);
    }

    public void MarkTorrentRemoved()
    {
        TorrentStatus = "Removed from qBittorrent";
    }

    private static string BuildTorrentStatus(string? state, double progress)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return string.Empty;
        }

        return string.Equals(state, "Removed from qBittorrent", StringComparison.OrdinalIgnoreCase)
            ? state
            : $"{state} ({progress:P0})";
    }

    private static EpisodeFetchCandidate? CreateSavedCandidate(TrackedEpisode episode)
    {
        if (string.IsNullOrWhiteSpace(episode.SelectedCandidateUrl) ||
            string.IsNullOrWhiteSpace(episode.SelectedCandidateName))
        {
            return null;
        }

        return new EpisodeFetchCandidate
        {
            EpisodeId = episode.Id,
            FileName = episode.SelectedCandidateName,
            FileUrl = episode.SelectedCandidateUrl,
            PluginName = episode.SelectedCandidatePlugin ?? string.Empty,
            FileSize = episode.SelectedCandidateFileSize,
            Seeders = episode.SelectedCandidateSeeders,
            QualityLabel = episode.SelectedCandidateQuality ?? string.Empty,
            AudioCodecLabel = episode.SelectedCandidateAudioCodec ?? string.Empty,
            QualityScore = string.IsNullOrWhiteSpace(episode.SelectedCandidateQuality) ? 0 : 1,
            TotalScore = episode.SelectedCandidateSeeders
        };
    }
}
