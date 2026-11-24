using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace media_management_app.ViewModels;

/// <summary>
/// Per-item "Refresh in Jellyfin": sends the link paths of a movie, episode, season or show
/// straight to Jellyfin (no debounce). For misses of the automatic notify on link.
/// </summary>
public partial class LibraryViewModel
{
    /// <summary>Commands stay disabled this long after the request returns.</summary>
    internal static readonly TimeSpan JellyfinRefreshCooldown = TimeSpan.FromSeconds(3);

    private IReadOnlyList<string> _lastLinkedPaths = [];

    [ObservableProperty]
    private bool isJellyfinRefreshBusy;

    /// <summary>Paths linked by the last Reconcile / Import, waiting for an optional refresh.</summary>
    [ObservableProperty]
    private int lastLinkedPathCount;

    public bool IsJellyfinRefreshConfigured => _jellyfinLibraryRefreshService.IsConfigured;

    public bool HasLastLinkedPaths => LastLinkedPathCount > 0;

    public string LastLinkedRefreshLabel => $"Refresh {LastLinkedPathCount} new link(s) in Jellyfin";

    partial void OnLastLinkedPathCountChanged(int value)
    {
        OnPropertyChanged(nameof(LastLinkedRefreshLabel));
        OnPropertyChanged(nameof(HasLastLinkedPaths));
        RefreshLastLinkedInJellyfinCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsJellyfinRefreshBusyChanged(bool value) => NotifyJellyfinRefreshCommands();

    private void NotifyJellyfinRefreshCommands()
    {
        OnPropertyChanged(nameof(IsJellyfinRefreshConfigured));
        RefreshEpisodeInJellyfinCommand.NotifyCanExecuteChanged();
        RefreshSeasonInJellyfinCommand.NotifyCanExecuteChanged();
        RefreshSelectedTitleInJellyfinCommand.NotifyCanExecuteChanged();
        RefreshLastLinkedInJellyfinCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRefreshEpisodeInJellyfin))]
    private Task RefreshEpisodeInJellyfin(LibraryEpisodeRowViewModel? episode)
    {
        if (episode is null)
        {
            return Task.CompletedTask;
        }

        var paths = JellyfinRefreshTargets.ForShow(
            episode.ShowTmdbId,
            _databaseService.GetSourceItems(),
            episode.SeasonNumber,
            episode.EpisodeNumber);
        return RunJellyfinRefreshAsync(episode.EpisodeCode, paths);
    }

    private bool CanRefreshEpisodeInJellyfin(LibraryEpisodeRowViewModel? episode) =>
        episode?.CanOpenInJellyfin == true && CanStartJellyfinRefresh();

    [RelayCommand(CanExecute = nameof(CanRefreshSeasonInJellyfin))]
    private Task RefreshSeasonInJellyfin(LibrarySeasonViewModel? season)
    {
        if (season is null || SelectedShow is null)
        {
            return Task.CompletedTask;
        }

        var paths = JellyfinRefreshTargets.ForShow(
            SelectedShow.TmdbId,
            _databaseService.GetSourceItems(),
            season.SeasonNumber);
        return RunJellyfinRefreshAsync(AppConstants.FormatSeasonDisplayName(season.SeasonNumber), paths);
    }

    private bool CanRefreshSeasonInJellyfin(LibrarySeasonViewModel? season) =>
        season?.HasJellyfinSymlink == true && SelectedShow is not null && CanStartJellyfinRefresh();

    [RelayCommand(CanExecute = nameof(CanRefreshSelectedTitleInJellyfin))]
    private Task RefreshSelectedTitleInJellyfin()
    {
        if (SelectedMovie is { HasJellyfinSymlink: true } movie)
        {
            return RunJellyfinRefreshAsync(
                movie.Title,
                JellyfinRefreshTargets.ForMovie(movie.TmdbId, _databaseService.GetSourceItems()));
        }

        if (SelectedShow is { HasJellyfinSymlink: true } show)
        {
            return RunJellyfinRefreshAsync(
                show.Title,
                JellyfinRefreshTargets.ForShow(show.TmdbId, _databaseService.GetSourceItems()));
        }

        return Task.CompletedTask;
    }

    private bool CanRefreshSelectedTitleInJellyfin() =>
        SelectedTitleHasJellyfinSymlink && CanStartJellyfinRefresh();

    [RelayCommand(CanExecute = nameof(CanRefreshLastLinkedInJellyfin))]
    private async Task RefreshLastLinkedInJellyfin()
    {
        var paths = _lastLinkedPaths;
        _lastLinkedPaths = [];
        LastLinkedPathCount = 0;
        await RunJellyfinRefreshAsync("New links", paths);
    }

    private bool CanRefreshLastLinkedInJellyfin() => HasLastLinkedPaths && CanStartJellyfinRefresh();

    private bool CanStartJellyfinRefresh() => !IsJellyfinRefreshBusy && IsJellyfinRefreshConfigured;

    private async Task RunJellyfinRefreshAsync(string label, IReadOnlyList<string> paths)
    {
        if (IsJellyfinRefreshBusy)
        {
            return;
        }

        IsJellyfinRefreshBusy = true;
        try
        {
            StatusMessage = $"Sending Jellyfin refresh for {label} ({paths.Count} path(s))...";
            var result = await _jellyfinLibraryRefreshService.RefreshNowAsync(paths);
            StatusMessage = $"{label}: {result.ToStatusText()}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Jellyfin refresh failed for {label}: {ex.Message}";
        }
        finally
        {
            await Task.Delay(JellyfinRefreshCooldown);
            IsJellyfinRefreshBusy = false;
        }
    }

    private HashSet<string> SnapshotLinkedSymlinkPaths() =>
        GetLinkedSymlinkPaths(_databaseService.GetSourceItems());

    private static HashSet<string> GetLinkedSymlinkPaths(IEnumerable<SourceItem> sourceItems) =>
        sourceItems
            .Where(item =>
                item.MatchAccepted &&
                item.State == ItemState.Linked &&
                !string.IsNullOrWhiteSpace(item.SymlinkPath))
            .Select(item => item.SymlinkPath!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// After Reconcile / Import: remember the link paths that are new since <paramref name="before"/>
    /// and offer one "Refresh in Jellyfin" for all of them next to the status line.
    /// </summary>
    private void OfferJellyfinRefreshForNewLinks(IReadOnlySet<string> before)
    {
        var added = GetLinkedSymlinkPaths(_databaseService.GetSourceItems())
            .Where(path => !before.Contains(path))
            .ToList();
        _lastLinkedPaths = added;
        LastLinkedPathCount = added.Count;
        NotifyJellyfinRefreshCommands();
    }
}
