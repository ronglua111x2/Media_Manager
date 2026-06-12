using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Common;

namespace media_management_app.ViewModels;

public sealed partial class TorrentOrderViewModel : ObservableObject
{
    private bool _isLoadingSelection;
    private long? _selectedCandidateId;

    public TorrentOrderViewModel()
    {
        AcceptCommand = new RelayCommand(Accept, () => CanAccept);
    }

    public long Id { get; init; }

    public MediaKind TargetKind { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public TorrentOrderStatus Status { get; init; } = TorrentOrderStatus.Draft;

    public string StatusDetail { get; init; } = string.Empty;

    public string SelectedCandidateName { get; init; } = string.Empty;

    public int SelectedCandidateSeeders { get; init; }

    public string SelectedCandidateQuality { get; init; } = string.Empty;

    public string TorrentName { get; init; } = string.Empty;

    public double TorrentProgress { get; init; }

    public ObservableCollection<TorrentOrderCandidateViewModel> Candidates { get; } = [];

    public IRelayCommand AcceptCommand { get; }

    public Action<long, long>? CandidateSelected { get; set; }

    public Action<long>? AcceptRequested { get; set; }

    public long? SelectedCandidateId
    {
        get => _selectedCandidateId;
        set
        {
            if (SetProperty(ref _selectedCandidateId, value))
            {
                OnPropertyChanged(nameof(SelectedCandidate));
                OnPropertyChanged(nameof(SelectedCandidateIsMultiSeason));
                OnPropertyChanged(nameof(SelectedCandidateCoveredSeasonsDisplay));
                OnPropertyChanged(nameof(SelectedCandidateMultiSeasonWarning));
                OnPropertyChanged(nameof(HasCandidates));
                OnPropertyChanged(nameof(CanAccept));
                AcceptCommand.NotifyCanExecuteChanged();
                if (!_isLoadingSelection && value is not null)
                {
                    CandidateSelected?.Invoke(Id, value.Value);
                }
            }
        }
    }

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

    public bool HasCandidates => Candidates.Count > 0;

    public TorrentOrderCandidateViewModel? SelectedCandidate =>
        SelectedCandidateId is null
            ? null
            : Candidates.FirstOrDefault(candidate => candidate.Id == SelectedCandidateId.Value);

    public bool SelectedCandidateIsMultiSeason => SelectedCandidate?.IsMultiSeason == true;

    public string SelectedCandidateCoveredSeasonsDisplay => SelectedCandidate?.CoveredSeasonsDisplay ?? string.Empty;

    public string SelectedCandidateMultiSeasonWarning => SelectedCandidate?.MultiSeasonWarningText ?? string.Empty;

    public bool CanAccept => HasCandidates &&
                             SelectedCandidateId is not null &&
                             Status is TorrentOrderStatus.CandidatesFound or TorrentOrderStatus.Approved;

    public bool CanAddToClient => Status == TorrentOrderStatus.Approved &&
                                  !string.IsNullOrWhiteSpace(SelectedCandidateName);

    public bool CanLinkOutput => Status is TorrentOrderStatus.AddedToClient or TorrentOrderStatus.Downloading or TorrentOrderStatus.Completed;

    public string DetailText => string.IsNullOrWhiteSpace(StatusDetail) ? Summary : StatusDetail;

    public string CandidateText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SelectedCandidateName))
            {
                return "No candidate selected";
            }

            var quality = string.IsNullOrWhiteSpace(SelectedCandidateQuality) ? "unknown" : SelectedCandidateQuality;
            return $"{SelectedCandidateName} | {quality} | {SelectedCandidateSeeders} seeders";
        }
    }

    public string TorrentText => string.IsNullOrWhiteSpace(TorrentName)
        ? string.Empty
        : $"{TorrentName} | {TorrentProgress:P0}";

    public void LoadCandidates(IEnumerable<TorrentOrderCandidateViewModel> candidates)
    {
        _isLoadingSelection = true;
        try
        {
            Candidates.Clear();
            foreach (var candidate in candidates)
            {
                Candidates.Add(candidate);
            }

            SelectedCandidateId = Candidates.FirstOrDefault(candidate => candidate.IsSelected)?.Id
                ?? Candidates.FirstOrDefault()?.Id;
        }
        finally
        {
            _isLoadingSelection = false;
        }

        OnPropertyChanged(nameof(HasCandidates));
        OnPropertyChanged(nameof(CanAccept));
        OnPropertyChanged(nameof(SelectedCandidate));
        OnPropertyChanged(nameof(SelectedCandidateIsMultiSeason));
        OnPropertyChanged(nameof(SelectedCandidateCoveredSeasonsDisplay));
        OnPropertyChanged(nameof(SelectedCandidateMultiSeasonWarning));
        AcceptCommand.NotifyCanExecuteChanged();
    }

    private void Accept()
    {
        AcceptRequested?.Invoke(Id);
    }
}
