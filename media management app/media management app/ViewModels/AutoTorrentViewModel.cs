using System.Collections.ObjectModel;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Models;
using media_management_app.Services;

namespace media_management_app.ViewModels;

public partial class AutoTorrentViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IQbittorrentClient _qbittorrentClient;
    private readonly IAppLogger _logger;

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string seasonNumber = string.Empty;

    [ObservableProperty]
    private string episodeNumber = string.Empty;

    [ObservableProperty]
    private string quality = "1080p";

    [ObservableProperty]
    private string rawQuery = string.Empty;

    [ObservableProperty]
    private TorrentSearchResult? selectedResult;

    [ObservableProperty]
    private string statusMessage = "Enter a query and search qBittorrent.";

    [ObservableProperty]
    private bool isBusy;

    public AutoTorrentViewModel(ISettingsService settingsService, IQbittorrentClient qbittorrentClient, IAppLogger logger)
    {
        _settingsService = settingsService;
        _qbittorrentClient = qbittorrentClient;
        _logger = logger;
        Results = [];
    }

    public ObservableCollection<TorrentSearchResult> Results { get; }

    [RelayCommand]
    private async Task TestConnection()
    {
        if (IsBusy)
        {
            return;
        }

        await RunAsync(async () =>
        {
            StatusMessage = "Testing qBittorrent connection...";
            var version = await _qbittorrentClient.TestConnectionAsync();
            StatusMessage = $"Connected to qBittorrent {version}.";
        });
    }

    [RelayCommand]
    private async Task Search()
    {
        if (IsBusy)
        {
            return;
        }

        var query = BuildQuery();
        if (string.IsNullOrWhiteSpace(query))
        {
            StatusMessage = "Enter a raw query or title/season/episode.";
            return;
        }

        await RunAsync(async () =>
        {
            Results.Clear();
            SelectedResult = null;
            StatusMessage = $"Searching: {query}";
            var results = await _qbittorrentClient.SearchAsync(new TorrentSearchRequest { Query = query });
            foreach (var result in results)
            {
                Results.Add(result);
            }

            StatusMessage = Results.Count == 0
                ? "Search completed with no results. Check qBittorrent search plugins."
                : $"Search completed. {Results.Count} result(s).";
        });
    }

    [RelayCommand]
    private async Task AddSelected()
    {
        if (IsBusy)
        {
            return;
        }

        if (SelectedResult is null)
        {
            StatusMessage = "Select a torrent result first.";
            return;
        }
        if (!SelectedResult.CanAdd)
        {
            StatusMessage = "This search result is not a supported qBittorrent URL.";
            return;
        }

        await RunAsync(async () =>
        {
            StatusMessage = $"Adding torrent: {SelectedResult.FileName}";
            var settings = _settingsService.Current.AutoTorrent;
            var addedTorrent = await _qbittorrentClient.AddTorrentAsync(new AddTorrentRequest
            {
                Url = SelectedResult.FileUrl,
                PluginName = SelectedResult.EngineName,
                SavePath = GetDownloadFolder(),
                Category = string.IsNullOrWhiteSpace(settings.CategoryName) ? "AutoTorrent" : settings.CategoryName,
                Paused = false
            });
            StatusMessage = $"Verified: {addedTorrent.Name} [{addedTorrent.State}] -> {addedTorrent.SavePath}";
        });
    }

    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        catch (HttpRequestException ex)
        {
            StatusMessage = "Cannot reach qBittorrent Web UI. Check that qBittorrent is running and Web UI is enabled.";
            _logger.Error(StatusMessage, ex, Common.LogTarget.All);
        }
        catch (TaskCanceledException ex)
        {
            StatusMessage = "qBittorrent request timed out.";
            _logger.Error(StatusMessage, ex, Common.LogTarget.All);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            _logger.Error($"Auto Torrent operation failed: {ex.Message}", ex, Common.LogTarget.All);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string BuildQuery()
    {
        if (!string.IsNullOrWhiteSpace(RawQuery))
        {
            return RawQuery.Trim();
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Title))
        {
            parts.Add(Title.Trim());
        }

        if (int.TryParse(SeasonNumber, out var season) && int.TryParse(EpisodeNumber, out var episode))
        {
            parts.Add($"S{season:00}E{episode:00}");
        }

        if (!string.IsNullOrWhiteSpace(Quality))
        {
            parts.Add(Quality.Trim());
        }

        return string.Join(' ', parts);
    }

    private string GetDownloadFolder()
    {
        var configuredFolder = _settingsService.Current.AutoTorrent.DownloadFolder;
        if (!string.IsNullOrWhiteSpace(configuredFolder))
        {
            return configuredFolder;
        }

        return _settingsService.Current.SourceFolders.FirstOrDefault() ?? string.Empty;
    }
}
