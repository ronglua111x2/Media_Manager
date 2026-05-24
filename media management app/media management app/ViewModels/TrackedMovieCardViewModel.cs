using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.ViewModels;

public partial class TrackedMovieCardViewModel : ObservableObject
{
    private static readonly IReadOnlyList<string> AvailableQualityOptions = ["2160p", "1080p", "720p", "480p"];
    private readonly Action<long, bool> _wantedChanged;
    private readonly Action<long, string, string, int> _savePreferences;

    public TrackedMovieCardViewModel(
        TrackedMovie movie,
        Action<long, bool> wantedChanged,
        Action<long, string, string, int> savePreferences)
    {
        Movie = movie;
        _wantedChanged = wantedChanged;
        _savePreferences = savePreferences;
        isWanted = movie.IsWanted;
        QualityOptions = new ObservableCollection<QualityOptionViewModel>(
            AvailableQualityOptions.Select(quality => new QualityOptionViewModel(
                quality,
                ParseSelectedQualities(movie.PreferredQuality).Contains(quality, StringComparer.OrdinalIgnoreCase),
                NotifyQualitySelectionChanged)));
        preferredAudioCodec = movie.PreferredAudioCodec;
        minimumSeedersText = movie.MinimumSeeders.ToString();
        torrentHash = movie.TorrentHash ?? string.Empty;
        torrentStatus = BuildTorrentStatus(movie.TorrentState, movie.TorrentProgress);
        Candidates = [];
        if (CreateSavedCandidate(movie) is { } savedCandidate)
        {
            Candidates.Add(new EpisodeCandidateViewModel(savedCandidate));
        }

        selectedCandidate = Candidates.FirstOrDefault();
    }

    public TrackedMovie Movie { get; }

    public long Id => Movie.Id;

    public string Title => Movie.DisplayTitle;

    public string Overview => Movie.Overview ?? string.Empty;

    public EpisodeAvailability Availability => Movie.Availability;

    public bool IsMissing => Movie.Availability == EpisodeAvailability.Missing;

    public string Stats => $"{Availability} | {(IsWanted ? "wanted" : "not wanted")} | selected {SelectedCandidateSizeDisplay}";

    public long SelectedCandidateBytes => SelectedCandidate?.Candidate.FileSize ?? 0;

    public string SelectedCandidateSizeDisplay => StorageStatusViewModel.FormatSize(SelectedCandidateBytes);

    public string PreferencesSummary =>
        $"Quality {SelectedQualitySummary} | Audio {(string.IsNullOrWhiteSpace(Movie.PreferredAudioCodec) ? "Any" : Movie.PreferredAudioCodec)} | Min seeders {Movie.MinimumSeeders}";

    public ObservableCollection<QualityOptionViewModel> QualityOptions { get; }

    public string SelectedQualitySummary
    {
        get
        {
            var selected = GetSelectedQualities().ToList();
            return selected.Count == 0 ? "Any" : string.Join(", ", selected);
        }
    }

    public ObservableCollection<EpisodeCandidateViewModel> Candidates { get; }

    [ObservableProperty]
    private bool isWanted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedCandidateBytes))]
    [NotifyPropertyChangedFor(nameof(SelectedCandidateSizeDisplay))]
    [NotifyPropertyChangedFor(nameof(Stats))]
    private EpisodeCandidateViewModel? selectedCandidate;

    [ObservableProperty]
    private string preferredAudioCodec;

    [ObservableProperty]
    private string minimumSeedersText;

    [ObservableProperty]
    private string preferenceStatus = string.Empty;

    [ObservableProperty]
    private string torrentHash;

    [ObservableProperty]
    private string torrentStatus;

    partial void OnIsWantedChanged(bool value)
    {
        _wantedChanged(Id, value);
        OnPropertyChanged(nameof(Stats));
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
    }

    public void MarkTorrentAdded(AddedTorrentResult torrent)
    {
        TorrentHash = torrent.Hash;
        TorrentStatus = BuildTorrentStatus(torrent.IsComplete ? "Downloaded" : torrent.State, torrent.Progress);
    }

    public void UpdateTorrentStatus(AddedTorrentResult torrent)
    {
        TorrentStatus = BuildTorrentStatus(torrent.IsComplete ? "Downloaded" : torrent.State, torrent.Progress);
    }

    public void MarkTorrentRemoved()
    {
        TorrentStatus = "Removed from qBittorrent";
    }

    [RelayCommand]
    private void SavePreferences()
    {
        if (!int.TryParse(MinimumSeedersText, out var minimumSeeders) || minimumSeeders < 0)
        {
            PreferenceStatus = "Minimum seeders must be 0 or greater.";
            return;
        }

        var quality = string.Join(',', GetSelectedQualities());
        _savePreferences(Id, quality, PreferredAudioCodec, minimumSeeders);
        Movie.PreferredQuality = quality;
        Movie.PreferredAudioCodec = string.IsNullOrWhiteSpace(PreferredAudioCodec) ? string.Empty : PreferredAudioCodec.Trim();
        Movie.MinimumSeeders = minimumSeeders;
        PreferenceStatus = "Saved.";
        OnPropertyChanged(nameof(PreferencesSummary));
    }

    private IEnumerable<string> GetSelectedQualities()
    {
        return QualityOptions.Where(option => option.IsSelected).Select(option => option.Label);
    }

    private void NotifyQualitySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedQualitySummary));
        OnPropertyChanged(nameof(PreferencesSummary));
    }

    private static HashSet<string> ParseSelectedQualities(string value)
    {
        var selected = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected.Count == 0 && !string.IsNullOrWhiteSpace(value))
        {
            selected.Add(value.Trim());
        }

        if (selected.Count == 0)
        {
            selected.Add("1080p");
        }

        return selected;
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

    private static EpisodeFetchCandidate? CreateSavedCandidate(TrackedMovie movie)
    {
        if (string.IsNullOrWhiteSpace(movie.SelectedCandidateUrl) ||
            string.IsNullOrWhiteSpace(movie.SelectedCandidateName))
        {
            return null;
        }

        return new EpisodeFetchCandidate
        {
            MovieId = movie.Id,
            FileName = movie.SelectedCandidateName,
            FileUrl = movie.SelectedCandidateUrl,
            PluginName = movie.SelectedCandidatePlugin ?? string.Empty,
            FileSize = movie.SelectedCandidateFileSize,
            Seeders = movie.SelectedCandidateSeeders,
            QualityLabel = movie.SelectedCandidateQuality ?? string.Empty,
            AudioCodecLabel = movie.SelectedCandidateAudioCodec ?? string.Empty,
            QualityScore = string.IsNullOrWhiteSpace(movie.SelectedCandidateQuality) ? 0 : 1,
            TotalScore = movie.SelectedCandidateSeeders
        };
    }
}
