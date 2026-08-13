using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using media_management_app.Common;
using media_management_app.Views;
using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfMessageBoxResult = System.Windows.MessageBoxResult;
using WpfPanel = System.Windows.Controls.Panel;
using WpfWindowState = System.Windows.WindowState;

namespace media_management_app.Services;

public sealed class JellyfinViewerService : IJellyfinViewerService
{
    private readonly ISettingsService _settingsService;
    private readonly IAppLogger _logger;
    private readonly object _environmentLock = new();
    private Task<CoreWebView2Environment>? _environmentTask;
    private JellyfinViewerWindow? _window;
    private WebView2? _webView;
    private bool _forceClose;

    public JellyfinViewerService(
        ISettingsService settingsService,
        IAppLogger logger,
        IAppLifecycleService lifecycleService)
    {
        _settingsService = settingsService;
        _logger = logger;
        lifecycleService.AppModeChanged += OnAppModeChanged;
    }

    public bool IsOpen => _window is { IsLoaded: true };

    public event EventHandler? IsOpenChanged;

    public void ShowOrActivate()
    {
        RunOnUi(ShowOrActivateCore);
    }

    public void Close(bool skipConfirm = false)
    {
        RunOnUi(() => CloseCore(skipConfirm));
    }

    private void ShowOrActivateCore()
    {
        if (!TryGetConfiguredUri(out var uri, out var errorMessage))
        {
            _logger.Warning($"Jellyfin viewer: {errorMessage}", LogTarget.All);
            return;
        }

        if (_window is not null)
        {
            if (_window.WindowState == WpfWindowState.Minimized)
            {
                _window.WindowState = WpfWindowState.Normal;
            }

            _window.Show();
            _window.Activate();
            return;
        }

        try
        {
            _window = new JellyfinViewerWindow();
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to create Jellyfin viewer window.", ex, LogTarget.All);
            _window = null;
            return;
        }

        _window.Closing += OnWindowClosing;
        _window.Closed += OnWindowClosed;
        _window.Show();
        RaiseIsOpenChanged();
        _ = InitializeWebViewAsync(uri, _window.BrowserHostPanel);
    }

    private void CloseCore(bool skipConfirm)
    {
        if (_window is null)
        {
            return;
        }

        _forceClose = skipConfirm;
        _window.Close();
    }

    private async Task InitializeWebViewAsync(Uri uri, WpfPanel host)
    {
        try
        {
            _webView = new WebView2();
            host.Children.Clear();
            host.Children.Add(_webView);

            var environment = await GetEnvironmentAsync();
            if (_webView is null || _window is null)
            {
                return;
            }

            await _webView.EnsureCoreWebView2Async(environment);
            if (_webView.CoreWebView2 is null || _window is null)
            {
                return;
            }

            _webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
            _webView.CoreWebView2.DownloadStarting += OnDownloadStarting;
            _webView.CoreWebView2.ContainsFullScreenElementChanged += OnContainsFullScreenElementChanged;
            _webView.CoreWebView2.Navigate(uri.AbsoluteUri);
        }
        catch (Exception ex)
        {
            _logger.Error("Jellyfin viewer WebView initialization failed.", ex, LogTarget.All);
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
        var userDataFolder = Path.Combine(_settingsService.Current.StateFolder, "WebView2", "Jellyfin");
        Directory.CreateDirectory(userDataFolder);
        return await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
        {
            return;
        }

        if (IsConfiguredJellyfinUri(uri))
        {
            _webView?.CoreWebView2?.Navigate(uri.AbsoluteUri);
            return;
        }

        if (uri.Scheme is "http" or "https")
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
        }
    }

    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        // Leave default save behavior so Dashboard log/file downloads work.
    }

    private void OnContainsFullScreenElementChanged(object? sender, object e)
    {
        RunOnUi(() =>
        {
            var fullscreen = _webView?.CoreWebView2?.ContainsFullScreenElement == true;
            _window?.SetHtmlFullscreen(fullscreen);
        });
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_forceClose || IsAppShuttingDown())
        {
            ReleaseWebView();
            return;
        }

        if (ShouldConfirmClose() && !UserConfirmedClose())
        {
            e.Cancel = true;
            return;
        }

        ReleaseWebView();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (_window is not null)
        {
            _window.Closing -= OnWindowClosing;
            _window.Closed -= OnWindowClosed;
        }

        _window = null;
        _forceClose = false;
        RaiseIsOpenChanged();
    }

    private void OnAppModeChanged(object? sender, AppMode mode)
    {
        if (mode != AppMode.Background)
        {
            return;
        }

        var autoClose = _settingsService.Current.AutoTrack?.Jellyfin?.AutoCloseViewerOnBackground == true;
        if (!autoClose)
        {
            return;
        }

        Close(skipConfirm: true);
    }

    private bool ShouldConfirmClose()
        => _settingsService.Current.AutoTrack?.Jellyfin?.ConfirmCloseViewer == true;

    private bool UserConfirmedClose()
    {
        var result = WpfMessageBox.Show(
            _window,
            "Close the Jellyfin window?",
            "Jellyfin",
            WpfMessageBoxButton.YesNo,
            WpfMessageBoxImage.Question);
        return result == WpfMessageBoxResult.Yes;
    }

    private static bool IsAppShuttingDown()
    {
        var app = WpfApplication.Current;
        return app is null || app.MainWindow is not { IsLoaded: true };
    }

    private bool TryGetConfiguredUri(out Uri uri, out string errorMessage)
    {
        var configuredUrl = _settingsService.Current.AutoTrack?.Jellyfin?.BaseUrl;
        if (string.IsNullOrWhiteSpace(configuredUrl)
            || !Uri.TryCreate(configuredUrl.Trim().TrimEnd('/'), UriKind.Absolute, out var parsedUri))
        {
            uri = new Uri("about:blank");
            errorMessage = "Jellyfin base URL is invalid. Check Integrations settings.";
            return false;
        }

        uri = parsedUri;
        errorMessage = string.Empty;
        return true;
    }

    private bool IsConfiguredJellyfinUri(Uri uri)
    {
        if (!TryGetConfiguredUri(out var configured, out _))
        {
            return false;
        }

        return string.Equals(uri.Scheme, configured.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Host, configured.Host, StringComparison.OrdinalIgnoreCase)
            && uri.Port == configured.Port;
    }

    private void ReleaseWebView()
    {
        if (_webView is null)
        {
            return;
        }

        if (_webView.CoreWebView2 is not null)
        {
            _webView.CoreWebView2.NewWindowRequested -= OnNewWindowRequested;
            _webView.CoreWebView2.DownloadStarting -= OnDownloadStarting;
            _webView.CoreWebView2.ContainsFullScreenElementChanged -= OnContainsFullScreenElementChanged;
        }

        if (_webView.Parent is WpfPanel parent)
        {
            parent.Children.Remove(_webView);
        }

        _webView.Dispose();
        _webView = null;
    }

    private void RaiseIsOpenChanged() => IsOpenChanged?.Invoke(this, EventArgs.Empty);

    private void RunOnUi(Action action)
    {
        var dispatcher = _window?.Dispatcher ?? WpfApplication.Current?.Dispatcher;
        if (dispatcher is null)
        {
            action();
            return;
        }

        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
    }
}
