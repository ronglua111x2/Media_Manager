using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.ViewModels;

public sealed partial class TorrentOrderViewModel : ObservableObject
{
    private bool _isLoadingSelection;
    private long? _selectedCandidateId;

    public TorrentOrderViewModel()
    {
        AcceptCommand = new RelayCommand(Accept, () => CanAccept);
        RetryAddCommand = new RelayCommand(RetryAdd, () => CanRetryAdd);
        RetrySearchCommand = new RelayCommand(RetrySearch, () => CanRetrySearch);
        ReconcilePackCommand = new RelayCommand(ReconcilePack, () => CanReconcilePack);
    }

    public static bool CartOperationRunning { get; set; }

    public long Id { get; init; }

    public long MediaId { get; init; }

    public MediaKind TargetKind { get; init; }

    public int? SeasonNumber { get; init; }

    public long? EpisodeId { get; init; }

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

    public IRelayCommand RetryAddCommand { get; }

    public IRelayCommand RetrySearchCommand { get; }

    public IRelayCommand ReconcilePackCommand { get; }

    public Action<long, long>? CandidateSelected { get; set; }

    public Action<long>? AcceptRequested { get; set; }

    public Action<long>? RetryAddRequested { get; set; }

    public Action<long>? RetrySearchRequested { get; set; }

    public Action<long>? ReconcilePackRequested { get; set; }

    public bool IsSeasonPackOrder => EpisodeId is null && SeasonNumber is not null && TargetKind != MediaKind.Movie;

    public bool IsPackTorrentComplete => TorrentProgress >= 0.999 && !string.IsNullOrWhiteSpace(TorrentName);

    public bool CanReconcilePack => IsSeasonPackOrder && IsPackTorrentComplete && !CartOperationRunning;

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
                OnPropertyChanged(nameof(SelectedCandidateContentProfileWarning));
                OnPropertyChanged(nameof(HasSelectedCandidateContentWarning));
                OnPropertyChanged(nameof(HasCandidates));
                OnPropertyChanged(nameof(CanAccept));
                OnPropertyChanged(nameof(CanRetryAdd));
                OnPropertyChanged(nameof(CanRetrySearch));
                AcceptCommand.NotifyCanExecuteChanged();
                RetryAddCommand.NotifyCanExecuteChanged();
                RetrySearchCommand.NotifyCanExecuteChanged();
                if (!_isLoadingSelection && value is not null)
                {
                    var orderId = Id;
                    var candidateId = value.Value;
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(
                        DispatcherPriority.Background,
                        () => CandidateSelected?.Invoke(orderId, candidateId));
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

    public string SelectedCandidateContentProfileWarning => SelectedCandidate?.ContentProfileWarning ?? string.Empty;

    public bool HasSelectedCandidateContentWarning => SelectedCandidate?.HasContentProfileWarning == true;

    public bool CanAccept => HasCandidates &&
                             SelectedCandidateId is not null &&
                             Status is TorrentOrderStatus.CandidatesFound or TorrentOrderStatus.Approved;

    public bool CanRetryAdd => !CartOperationRunning &&
                               Status == TorrentOrderStatus.Failed &&
                               SelectedCandidateId is not null &&
                               !string.IsNullOrWhiteSpace(SelectedCandidateName);

    public bool CanRetrySearch => !CartOperationRunning &&
                                  Status is TorrentOrderStatus.Failed or TorrentOrderStatus.NoCandidates;

    public bool CanShowRecoveryMenu => CanRetryAdd || CanRetrySearch;

    public bool CanChangeCandidate =>
        HasCandidates &&
        !CartOperationRunning &&
        Status is not TorrentOrderStatus.Searching
            and not TorrentOrderStatus.Downloading
            and not TorrentOrderStatus.Completed
            and not TorrentOrderStatus.AddedToClient;

    public int CandidateCount => Candidates.Count;

    public bool CanAddToClient => Status == TorrentOrderStatus.Approved &&
                                  !string.IsNullOrWhiteSpace(SelectedCandidateName);

    public bool CanLinkOutput => Status is TorrentOrderStatus.AddedToClient or TorrentOrderStatus.Downloading or TorrentOrderStatus.Completed;

    public string DetailText => string.IsNullOrWhiteSpace(StatusDetail) ? Summary : StatusDetail;

    public bool HasPackInspectionLines => StatusDetail.Contains('\n', StringComparison.Ordinal);

    private PackInspectionDisplay PackInspection => PackInspectionDisplay.ParseStatusDetail(StatusDetail);

    public string InspectionCoversLine =>
        HasPackInspectionLines ? PackInspection.CoversLine : DetailText;

    public string InspectionExtrasLine =>
        HasPackInspectionLines ? PackInspection.ExtrasLine : string.Empty;

    public string InspectionMoviesLine =>
        HasPackInspectionLines ? PackInspection.MoviesLine : string.Empty;

    public bool HasInspectionExtrasLine => HasPackInspectionLines && PackInspection.HasExtrasLine;

    public bool HasInspectionMoviesLine => HasPackInspectionLines && PackInspection.HasMoviesLine;

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
        OnPropertyChanged(nameof(CanRetryAdd));
        OnPropertyChanged(nameof(CanRetrySearch));
        OnPropertyChanged(nameof(SelectedCandidate));
        OnPropertyChanged(nameof(SelectedCandidateIsMultiSeason));
        OnPropertyChanged(nameof(SelectedCandidateCoveredSeasonsDisplay));
        OnPropertyChanged(nameof(SelectedCandidateMultiSeasonWarning));
        OnPropertyChanged(nameof(SelectedCandidateContentProfileWarning));
        OnPropertyChanged(nameof(HasSelectedCandidateContentWarning));
        AcceptCommand.NotifyCanExecuteChanged();
        RetryAddCommand.NotifyCanExecuteChanged();
        RetrySearchCommand.NotifyCanExecuteChanged();
    }

    private void Accept()
    {
        AcceptRequested?.Invoke(Id);
    }

    private void RetryAdd()
    {
        RetryAddRequested?.Invoke(Id);
    }

    private void RetrySearch()
    {
        RetrySearchRequested?.Invoke(Id);
    }

    private void ReconcilePack()
    {
        ReconcilePackRequested?.Invoke(Id);
    }

    public void NotifyOperationRunningChanged()
    {
        OnPropertyChanged(nameof(CanChangeCandidate));
        OnPropertyChanged(nameof(CanShowRecoveryMenu));
        OnPropertyChanged(nameof(CanAccept));
        OnPropertyChanged(nameof(CanRetryAdd));
        OnPropertyChanged(nameof(CanRetrySearch));
        OnPropertyChanged(nameof(CanReconcilePack));
        AcceptCommand.NotifyCanExecuteChanged();
        RetryAddCommand.NotifyCanExecuteChanged();
        RetrySearchCommand.NotifyCanExecuteChanged();
        ReconcilePackCommand.NotifyCanExecuteChanged();
    }
}
