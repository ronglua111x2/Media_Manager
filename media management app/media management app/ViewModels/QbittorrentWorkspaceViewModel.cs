using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Services;
using WpfPanel = System.Windows.Controls.Panel;

namespace media_management_app.ViewModels;

public sealed partial class QbittorrentWorkspaceViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IQbittorrentWebViewHostService _webViewHostService;

    public QbittorrentWorkspaceViewModel(
        ISettingsService settingsService,
        IQbittorrentWebViewHostService webViewHostService)
    {
        _settingsService = settingsService;
        _webViewHostService = webViewHostService;
        webUiUrl = GetConfiguredUrl();
        statusMessage = "qBittorrent Web UI will load from your System Settings URL.";
        _webViewHostService.StatusChanged += OnWebViewStatusChanged;
    }

    [ObservableProperty]
    private string webUiUrl;

    [ObservableProperty]
    private string statusMessage;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool isReady;

    [ObservableProperty]
    private bool hasError;

    public async Task AttachBrowserAsync(WpfPanel host, CancellationToken cancellationToken = default)
    {
        RefreshConfiguredUrl();
        await _webViewHostService.InitializeAsync(host, cancellationToken);
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        RefreshConfiguredUrl();
        await _webViewHostService.NavigateToConfiguredUrlAsync();
    }

    [RelayCommand]
    private void RefreshSettings()
    {
        RefreshConfiguredUrl();
        StatusMessage = "qBittorrent settings refreshed.";
    }

    [RelayCommand]
    private void OpenExternal()
    {
        RefreshConfiguredUrl();
        if (!Uri.TryCreate(WebUiUrl, UriKind.Absolute, out var uri))
        {
            StatusMessage = "qBittorrent Web UI URL is invalid. Check System Settings.";
            HasError = true;
            return;
        }

        Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
        {
            UseShellExecute = true
        });
    }

    private void OnWebViewStatusChanged(object? sender, QbittorrentWebViewStatusChangedEventArgs e)
    {
        StatusMessage = e.Message;
        IsLoading = e.IsLoading;
        IsReady = e.IsReady;
        HasError = e.HasError;
        if (!string.IsNullOrWhiteSpace(e.CurrentUrl))
        {
            WebUiUrl = e.CurrentUrl;
        }
    }

    private void RefreshConfiguredUrl()
    {
        WebUiUrl = GetConfiguredUrl();
    }

    private string GetConfiguredUrl()
    {
        return string.IsNullOrWhiteSpace(_settingsService.Current.AutoTorrent.QbittorrentWebUiUrl)
            ? "http://localhost:8080"
            : _settingsService.Current.AutoTorrent.QbittorrentWebUiUrl.Trim();
    }
}
