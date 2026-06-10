using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using media_management_app.Common;
using WpfPanel = System.Windows.Controls.Panel;

namespace media_management_app.Services;

public sealed class QbittorrentWebViewHostService : IQbittorrentWebViewHostService
{
    private readonly ISettingsService _settingsService;
    private readonly IAppLogger _logger;
    private readonly object _environmentLock = new();
    private Task<CoreWebView2Environment>? _environmentTask;
    private WebView2? _webView;
    private Task? _initializationTask;
    private WpfPanel? _currentHost;

    public QbittorrentWebViewHostService(ISettingsService settingsService, IAppLogger logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    public event EventHandler<QbittorrentWebViewStatusChangedEventArgs>? StatusChanged;

    public string? CurrentUrl { get; private set; }

    public Task InitializeAsync(WpfPanel host, CancellationToken cancellationToken = default)
    {
        if (!host.Dispatcher.CheckAccess())
        {
            return host.Dispatcher
                .InvokeAsync(() => InitializeAsync(host, cancellationToken))
                .Task
                .Unwrap();
        }

        EnsureWebView(host);
        if (_initializationTask is null || _initializationTask.IsFaulted)
        {
            _initializationTask = InitializeCoreAsync(cancellationToken);
        }

        return _initializationTask;
    }

    public async Task NavigateToConfiguredUrlAsync(CancellationToken cancellationToken = default)
    {
        if (_currentHost is null)
        {
            UpdateStatus("qBittorrent WebView is not ready yet.", isLoading: false, isReady: false, hasError: true);
            return;
        }

        await InitializeAsync(_currentHost, cancellationToken);
        NavigateToConfiguredUrl();
    }

    public void Reload()
    {
        if (_webView?.CoreWebView2 is null)
        {
            UpdateStatus("qBittorrent WebView is still initializing.", isLoading: true, isReady: false, hasError: false);
            return;
        }

        UpdateStatus("Reloading qBittorrent Web UI...", isLoading: true, isReady: false, hasError: false);
        _webView.Reload();
    }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        if (_webView is null)
        {
            return;
        }

        try
        {
            UpdateStatus("Initializing qBittorrent Web UI...", isLoading: true, isReady: false, hasError: false);
            var environment = await GetEnvironmentAsync();
            cancellationToken.ThrowIfCancellationRequested();
            await _webView.EnsureCoreWebView2Async(environment);
            cancellationToken.ThrowIfCancellationRequested();
            NavigateToConfiguredUrl();
        }
        catch (OperationCanceledException)
        {
            UpdateStatus("qBittorrent Web UI initialization was canceled.", isLoading: false, isReady: false, hasError: true);
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("qBittorrent WebView initialization failed.", ex, LogTarget.All);
            UpdateStatus($"qBittorrent Web UI failed to initialize: {ex.Message}", isLoading: false, isReady: false, hasError: true);
        }
    }

    private Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        lock (_environmentLock)
        {
            _environmentTask ??= CreateEnvironmentAsync();
            return _environmentTask;
        }
    }

    private async Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        var userDataFolder = Path.Combine(_settingsService.Current.StateFolder, "WebView2", "qBittorrent");
        Directory.CreateDirectory(userDataFolder);
        return await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
    }

    private void EnsureWebView(WpfPanel host)
    {
        if (_webView is null)
        {
            _webView = new WebView2();
            _webView.NavigationStarting += (_, _) =>
                UpdateStatus("Loading qBittorrent Web UI...", isLoading: true, isReady: false, hasError: false);
            _webView.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                {
                    UpdateStatus("qBittorrent Web UI loaded.", isLoading: false, isReady: true, hasError: false);
                    return;
                }

                UpdateStatus(
                    $"qBittorrent Web UI load failed: {args.WebErrorStatus}.",
                    isLoading: false,
                    isReady: false,
                    hasError: true);
            };
        }

        AttachToHost(host);
    }

    private void AttachToHost(WpfPanel host)
    {
        if (_webView is null)
        {
            return;
        }

        if (_webView.Parent is WpfPanel existingHost && !ReferenceEquals(existingHost, host))
        {
            existingHost.Children.Remove(_webView);
        }

        if (!host.Children.Contains(_webView))
        {
            host.Children.Clear();
            host.Children.Add(_webView);
        }

        _currentHost = host;
    }

    private void NavigateToConfiguredUrl()
    {
        if (_webView?.CoreWebView2 is null)
        {
            return;
        }

        if (!TryGetConfiguredUri(out var uri, out var errorMessage))
        {
            UpdateStatus(errorMessage, isLoading: false, isReady: false, hasError: true);
            return;
        }

        CurrentUrl = uri.AbsoluteUri;
        UpdateStatus($"Opening {CurrentUrl}", isLoading: true, isReady: false, hasError: false);
        _webView.CoreWebView2.Navigate(CurrentUrl);
    }

    private bool TryGetConfiguredUri(out Uri uri, out string errorMessage)
    {
        var configuredUrl = _settingsService.Current.AutoTorrent.QbittorrentWebUiUrl;
        if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out var parsedUri))
        {
            uri = new Uri("about:blank");
            errorMessage = "qBittorrent Web UI URL is invalid. Check System Settings.";
            return false;
        }

        var builder = new UriBuilder(parsedUri);
        if (!builder.Path.EndsWith('/'))
        {
            builder.Path += "/";
        }

        uri = builder.Uri;
        errorMessage = string.Empty;
        return true;
    }

    private void UpdateStatus(string message, bool isLoading, bool isReady, bool hasError)
    {
        StatusChanged?.Invoke(
            this,
            new QbittorrentWebViewStatusChangedEventArgs
            {
                Message = message,
                IsLoading = isLoading,
                IsReady = isReady,
                HasError = hasError,
                CurrentUrl = CurrentUrl
            });
    }
}
