using System.Windows;
using media_management_app.Common;
using media_management_app.Models;
using Microsoft.Toolkit.Uwp.Notifications;

namespace media_management_app.Services;

public sealed class WindowsNotificationService : IWindowsNotificationService
{
    private readonly ITrayIconService _trayIconService;
    private readonly IAppLogger _logger;
    private bool _initialized;

    public WindowsNotificationService(ITrayIconService trayIconService, IAppLogger logger)
    {
        _trayIconService = trayIconService;
        _logger = logger;
    }

    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        ToastNotificationManagerCompat.OnActivated += HandleToastActivated;
        _initialized = true;
        _logger.Info("Windows notification service initialized.", LogTarget.All);
    }

    public bool TryShow(string title, string message, string? tag = null)
    {
        return TryShow(new WindowsNotificationRequest
        {
            Title = title,
            Message = message,
            Tag = tag
        });
    }

    public bool TryShow(WindowsNotificationRequest request)
    {
        if (!_initialized)
        {
            Initialize();
        }

        var safeTitle = string.IsNullOrWhiteSpace(request.Title) ? "Media Manager" : request.Title.Trim();
        var safeMessage = string.IsNullOrWhiteSpace(request.Message) ? "Notification" : request.Message.Trim();
        var tag = string.IsNullOrWhiteSpace(request.Tag)
            ? Guid.NewGuid().ToString("N")
            : request.Tag.Trim();
        var group = string.IsNullOrWhiteSpace(request.Group) ? "MediaManager" : request.Group.Trim();

        try
        {
            var builder = new ToastContentBuilder()
                .AddArgument("action", "openApp")
                .AddArgument("tag", tag)
                .AddText(safeTitle)
                .AddText(safeMessage);

            if (TryResolveImageUri(request.HeroImagePathOrUrl, out var heroUri))
            {
                builder.AddHeroImage(heroUri);
            }

            if (TryResolveImageUri(request.AppLogoOverridePathOrUrl, out var logoUri))
            {
                builder.AddAppLogoOverride(logoUri, ToastGenericAppLogoCrop.Default);
            }

            builder.Show(toast =>
            {
                toast.Tag = tag;
                toast.Group = group;
            });

            _logger.Info(
                $"Windows notification sent. Title='{safeTitle}', Tag='{tag}', Group='{group}', HeroImage='{request.HeroImagePathOrUrl ?? "(none)"}'.",
                LogTarget.All);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to send Windows notification. Title='{safeTitle}'. {ex.Message}", LogTarget.All);
            return false;
        }
    }

    private bool TryResolveImageUri(string? pathOrUrl, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(pathOrUrl))
        {
            return false;
        }

        var value = pathOrUrl.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var absoluteUri)
            && (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps))
        {
            uri = absoluteUri;
            return true;
        }

        if (!File.Exists(value))
        {
            _logger.Warning($"Notification image not found: '{value}'", LogTarget.All);
            return false;
        }

        uri = new Uri(Path.GetFullPath(value));
        return true;
    }

    private void HandleToastActivated(ToastNotificationActivatedEventArgsCompat args)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(ActivateMainWindow);
        _logger.Info("Windows notification activated. Bringing app to foreground.", LogTarget.All);
    }

    private void ActivateMainWindow()
    {
        if (_trayIconService.IsInitialized)
        {
            _trayIconService.RestoreFromTray();
            return;
        }

        if (System.Windows.Application.Current.MainWindow is not MainWindow mainWindow)
        {
            return;
        }

        mainWindow.ShowInTaskbar = true;
        mainWindow.Visibility = Visibility.Visible;
        if (mainWindow.WindowState == WindowState.Minimized)
        {
            mainWindow.WindowState = WindowState.Normal;
        }

        mainWindow.Show();
        mainWindow.Activate();
    }
}
