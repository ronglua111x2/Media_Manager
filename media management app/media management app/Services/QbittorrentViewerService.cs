using System.ComponentModel;
using System.Diagnostics;
using MahApps.Metro.IconPacks;
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

public sealed class QbittorrentViewerService : IQbittorrentViewerService
{
    private const string SearchUiGuardScript = """
        (() => {
          const guardKey = "__mediaManagerQbittorrentSearchGuard";
          const styleId = "media-manager-qbittorrent-search-guard";
          const classicSearchSelector = "#searchTabLink, #showSearchEngineLink";

          const isSearchRoute = () =>
            /^#\/search(?:[/?]|$)/i.test(window.location.hash);

          const redirectFromSearch = () => {
            if (!isSearchRoute()) {
              return;
            }

            window.location.replace(
              `${window.location.pathname}${window.location.search}#/`);
          };

          const ensureStyle = () => {
            if (document.getElementById(styleId)) {
              return;
            }

            const parent = document.head ?? document.documentElement;
            if (!parent) {
              return;
            }

            const style = document.createElement("style");
            style.id = styleId;
            style.textContent = `
              #searchTabLink,
              #showSearchEngineLink,
              #searchTabColumn,
              a[href="#/search"],
              a[href="./#/search"],
              a[href^="#/search/"],
              a[href^="#/search?"],
              a[href^="./#/search/"],
              a[href^="./#/search?"] {
                display: none !important;
              }
            `;
            parent.appendChild(style);
          };

          const showTransfersIfSearchIsSelected = () => {
            const searchTab = document.getElementById("searchTabLink");
            if (!searchTab?.classList.contains("selected")) {
              return;
            }

            document.getElementById("transfersTabLink")?.click();
          };

          const blockClassicSearchEntry = (event) => {
            const target = event.target;
            if (!(target instanceof Element)
              || !target.closest(classicSearchSelector)) {
              return;
            }

            event.preventDefault();
            event.stopImmediatePropagation();
            document.getElementById("transfersTabLink")?.click();
          };

          const applyGuard = () => {
            ensureStyle();
            showTransfersIfSearchIsSelected();
            redirectFromSearch();
          };

          if (window[guardKey]) {
            applyGuard();
            return;
          }

          window[guardKey] = true;
          document.addEventListener("click", blockClassicSearchEntry, true);
          document.addEventListener("DOMContentLoaded", applyGuard, { once: true });
          window.addEventListener("hashchange", applyGuard);
          window.addEventListener("popstate", applyGuard);
          window.addEventListener("pageshow", applyGuard);
          applyGuard();
        })();
        """;

    private readonly ISettingsService _settingsService;
    private readonly IAppLogger _logger;
    private readonly ICrashLogService _crashLog;
    private readonly object _environmentLock = new();
    private Task<CoreWebView2Environment>? _environmentTask;
    private WebViewerWindow? _window;
    private WebView2? _webView;
    private bool _forceClose;

    public QbittorrentViewerService(
        ISettingsService settingsService,
        IAppLogger logger,
        ICrashLogService crashLog,
        IAppLifecycleService lifecycleService)
    {
        _settingsService = settingsService;
        _logger = logger;
        _crashLog = crashLog;
        lifecycleService.AppModeChanged += OnAppModeChanged;
    }

    public bool IsOpen => _window is { IsLoaded: true };

    public event EventHandler? IsOpenChanged;

    public void ShowOrActivate(object? chromeDataContext = null)
    {
        RunOnUi(() => ShowOrActivateCore(chromeDataContext));
    }

    public void Close(bool skipConfirm = false)
    {
        RunOnUi(() => CloseCore(skipConfirm));
    }

    private void ShowOrActivateCore(object? chromeDataContext)
    {
        if (!TryGetConfiguredUri(out var uri, out var errorMessage))
        {
            _logger.Warning($"qBittorrent viewer: {errorMessage}", LogTarget.All);
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
            _window = new WebViewerWindow("qBittorrent", PackIconLucideKind.Globe, "AppBrushAccent")
            {
                DataContext = chromeDataContext
            };
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to create qBittorrent viewer window.", ex, LogTarget.All);
            _window = null;
            return;
        }

        _window.Closing += OnWindowClosing;
        _window.Closed += OnWindowClosed;
        _window.ReloadRequested += OnReloadRequested;
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

            await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(SearchUiGuardScript);
            _webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
            _webView.CoreWebView2.SourceChanged += OnSourceChanged;
            _webView.CoreWebView2.ProcessFailed += OnProcessFailed;
            _webView.CoreWebView2.Navigate(uri.AbsoluteUri);
            _window.SetCurrentUrl(uri.AbsoluteUri);
        }
        catch (Exception ex)
        {
            _logger.Error("qBittorrent viewer WebView initialization failed.", ex, LogTarget.All);
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

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
        {
            return;
        }

        if (IsConfiguredQbittorrentUri(uri))
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

    private void OnReloadRequested(object? sender, EventArgs e)
    {
        if (_webView?.CoreWebView2 is not null)
        {
            _webView.Reload();
            return;
        }

        if (_window is null)
        {
            return;
        }

        if (!TryGetConfiguredUri(out var uri, out var errorMessage))
        {
            _logger.Warning($"qBittorrent viewer: {errorMessage}", LogTarget.All);
            return;
        }

        _ = InitializeWebViewAsync(uri, _window.BrowserHostPanel);
    }

    private void OnSourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        RunOnUi(() => _window?.SetCurrentUrl(_webView?.CoreWebView2?.Source));
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        _crashLog.LogWebViewProcessFailed("qBittorrent", e, _webView?.CoreWebView2?.Source);
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
            _window.ReloadRequested -= OnReloadRequested;
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

        var autoClose = _settingsService.Current.AutoTorrent?.AutoCloseViewerOnBackground == true;
        if (!autoClose)
        {
            return;
        }

        Close(skipConfirm: true);
    }

    private bool ShouldConfirmClose()
        => _settingsService.Current.AutoTorrent?.ConfirmCloseViewer == true;

    private bool UserConfirmedClose()
    {
        var result = WpfMessageBox.Show(
            _window,
            "Close the qBittorrent window?",
            "qBittorrent",
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
        var configuredUrl = _settingsService.Current.AutoTorrent?.QbittorrentWebUiUrl;
        if (string.IsNullOrWhiteSpace(configuredUrl)
            || !Uri.TryCreate(configuredUrl.Trim(), UriKind.Absolute, out var parsedUri))
        {
            uri = new Uri("about:blank");
            errorMessage = "qBittorrent Web UI URL is invalid. Check Integrations settings.";
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

    private bool IsConfiguredQbittorrentUri(Uri uri)
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
            _webView.CoreWebView2.SourceChanged -= OnSourceChanged;
            _webView.CoreWebView2.ProcessFailed -= OnProcessFailed;
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
