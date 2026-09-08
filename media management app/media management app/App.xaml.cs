using System.Windows;
using System.Net.Http;
using System.Threading;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Win32;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;
using media_management_app.Services.Backup;
using media_management_app.Services.Events;
using media_management_app.Services.Gemini;
using media_management_app.Services.Symlink;
using media_management_app.Migrations;
using media_management_app.ViewModels;

namespace media_management_app;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\MediaManager.SingleInstance";

    private ServiceProvider? _serviceProvider;
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out _ownsSingleInstanceMutex);
        if (!_ownsSingleInstanceMutex)
        {
            System.Windows.MessageBox.Show(
                "Media Manager is already running.",
                "Media Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        var settings = _serviceProvider.GetRequiredService<ISettingsService>();
        settings.Load();

        var crashLog = _serviceProvider.GetRequiredService<ICrashLogService>();
        crashLog.ReportUncleanShutdownIfNeeded();
        crashLog.MarkAlive();
        StartCrashLoggingHooks();

        var geminiModelCatalog = _serviceProvider.GetRequiredService<IGeminiModelCatalogService>();
        geminiModelCatalog.ReloadFromDisk();
        NormalizeGeminiSettings(settings, geminiModelCatalog);

        _serviceProvider.GetRequiredService<IThemeService>().Apply(settings.Current.Ui?.Theme ?? AppTheme.Light);

        var database = _serviceProvider.GetRequiredService<IDatabaseService>();
        try
        {
            database.Initialize(settings.Current.StateFolder);
        }
        catch (DatabaseMigrationException)
        {
            Shutdown();
            return;
        }

        _serviceProvider.GetRequiredService<ILogCleanupService>().Start();
        PublishJunkCleanup.Run(
            AppContext.BaseDirectory,
            _serviceProvider.GetRequiredService<IAppLogger>());

        var symlinkCoordinator = _serviceProvider.GetRequiredService<ISymlinkCoordinatorService>();
        symlinkCoordinator.Start();

        var autoTrackScheduler = _serviceProvider.GetRequiredService<IAutoTrackSchedulerService>();
        var trayIconService = _serviceProvider.GetRequiredService<ITrayIconService>();
        autoTrackScheduler.RunCompleted += (_, result) => trayIconService.ShowAutoTrackRunCompleted(result);
        autoTrackScheduler.Start();

        _serviceProvider.GetRequiredService<IBackupSchedulerService>().Start();

        _serviceProvider.GetRequiredService<IWindowsNotificationService>().Initialize();
        WarmupPosterCache();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        var startup = settings.Current.Startup;
        var launchedFromToast = ToastNotificationManagerCompat.WasCurrentProcessToastActivated();

        if (startup.StartMinimized || startup.CloseToTray)
        {
            trayIconService.Initialize(mainWindow);
        }

        if (launchedFromToast)
        {
            mainWindow.ShowInTaskbar = false;
            mainWindow.Visibility = Visibility.Hidden;
            mainWindow.Show();
        }
        else if (startup.StartMinimized)
        {
            mainWindow.ShowInTaskbar = false;
            mainWindow.Visibility = Visibility.Hidden;
            mainWindow.Show();
            trayIconService.HideToTray();
        }
        else
        {
            mainWindow.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        StopCrashLoggingHooks();
        try
        {
            _serviceProvider?.GetService<ICrashLogService>()?.ClearAlive();
        }
        catch (Exception)
        {
        }

        try
        {
            _serviceProvider?.GetService<IAutoTrackSchedulerService>()?.Dispose();
            _serviceProvider?.GetService<IBackupSchedulerService>()?.Dispose();
            _serviceProvider?.GetService<ISymlinkCoordinatorService>()?.Dispose();
            _serviceProvider?.GetService<IJellyfinLibraryRefreshService>()?.Dispose();
            _serviceProvider?.GetService<ILogCleanupService>()?.Dispose();
            _serviceProvider?.GetService<ITrayIconService>()?.Dispose();
        }
        catch (Exception)
        {
        }

        _serviceProvider?.Dispose();
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder => builder.AddDebug());
        services.AddSingleton<HttpClient>();

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IAppLifecycleService, AppLifecycleService>();
        services.AddSingleton<IWindowsStartupService, WindowsStartupService>();
        services.AddSingleton<ITrayIconService, TrayIconService>();
        services.AddSingleton<IWindowsNotificationService, WindowsNotificationService>();
        services.AddSingleton<IAppLogger, AppLogger>();
        services.AddSingleton<ICrashLogService, CrashLogService>();
        services.AddSingleton<ILogCleanupService, LogCleanupService>();
        services.AddSingleton<IOperationProgressService, OperationProgressService>();
        services.AddSingleton<IDatabaseService, DatabaseService>();
        services.AddSingleton<IParserService, ParserService>();
        services.AddSingleton<IScannerService, ScannerService>();
        services.AddSingleton<ILibraryPathResolver, LibraryPathResolver>();
        services.AddSingleton<ILibraryLinkEventHub, LibraryLinkEventHub>();
        services.AddSingleton<IHardlinkService, HardlinkService>();
        services.AddSingleton<ISymlinkService, SymlinkService>();
        services.AddSingleton<ISymlinkSyncService, SymlinkSyncService>();
        services.AddSingleton<INfoWriterService, NfoWriterService>();
        services.AddSingleton<IJellyfinClient, JellyfinClient>();
        services.AddSingleton<IJellyfinLibraryRefreshService, JellyfinLibraryRefreshService>();
        services.AddSingleton<ISymlinkCoordinatorService, SymlinkCoordinatorService>();
        services.AddSingleton<ISourceReconciliationService, SourceReconciliationService>();
        services.AddSingleton<IQbittorrentClient, QbittorrentClient>();
        services.AddSingleton<IQbittorrentSearchPluginService, QbittorrentSearchPluginService>();
        services.AddSingleton<IQbittorrentProcessRestartService, QbittorrentProcessRestartService>();
        services.AddSingleton<IWarpCliService, WarpCliService>();
        services.AddSingleton<IRecipeService, RecipeService>();
        services.AddSingleton<ISearchTitleResolver, SearchTitleResolver>();
        services.AddSingleton<ISearchPlanBuilder, SearchPlanBuilder>();
        services.AddSingleton<ICandidateEvaluationService, CandidateEvaluationService>();
        services.AddSingleton<IAutomationFlowService, AutomationFlowService>();
        services.AddSingleton<IDeviceStatusService, DeviceStatusService>();
        services.AddSingleton<IDownloadFolderCatalogService, DownloadFolderCatalogService>();
        services.AddSingleton<ITorrentAddDiskAssignmentService, TorrentAddDiskAssignmentService>();
        services.AddSingleton<IConsoleWindowService, ConsoleWindowService>();
        services.AddSingleton<IJellyfinViewerService, JellyfinViewerService>();
        services.AddSingleton<IQbittorrentViewerService, QbittorrentViewerService>();
        services.AddSingleton<ShowSearchSnapshotService>();
        services.AddSingleton<TmdbMetadataProvider>();
        services.AddSingleton<IMetadataProvider>(provider => provider.GetRequiredService<TmdbMetadataProvider>());
        services.AddSingleton<ITmdbShowCatalogService>(provider => provider.GetRequiredService<TmdbMetadataProvider>());
        services.AddSingleton<ITmdbMovieCatalogService>(provider => provider.GetRequiredService<TmdbMetadataProvider>());
        services.AddSingleton<GeminiQuotaTracker>();
        services.AddSingleton<IGeminiModelCatalogService, GeminiModelCatalogService>();
        services.AddSingleton<IGeminiApiClient, GeminiApiClient>();
        services.AddSingleton<GeminiSpecialMappingProvider>();
        services.AddSingleton<SpecialMappingCache>();
        services.AddSingleton<ISpecialMappingOrchestrator, SpecialMappingOrchestrator>();
        services.AddSingleton<IGeminiLinkConfirmationService, GeminiLinkConfirmationService>();
        services.AddSingleton<ITrackedShowService, TrackedShowService>();
        services.AddSingleton<ITrackedMovieService, TrackedMovieService>();
        services.AddSingleton<IMediaMetadataSyncService, MediaMetadataSyncService>();
        services.AddSingleton<IPosterImageService, PosterImageService>();
        services.AddSingleton<ITorrentCartService, TorrentCartService>();
        services.AddSingleton<IMediaCardCatalogService, MediaCardCatalogService>();
        services.AddSingleton<IMediaImportService, MediaImportService>();
        services.AddSingleton<IFetchJobService, FetchJobService>();
        services.AddSingleton<IAutoTorrentLinkService, AutoTorrentLinkService>();
        services.AddSingleton<IPackLinkCoordinatorService, PackLinkCoordinatorService>();
        services.AddSingleton<ITorrentReconciliationService, TorrentReconciliationService>();
        services.AddSingleton<ILibraryManagementService, LibraryManagementService>();
        services.AddSingleton<IAutoTrackService, AutoTrackService>();
        services.AddSingleton<AutoTrackCandidatePolicyService>();
        services.AddSingleton<IAutoTrackSchedulerService, AutoTrackSchedulerService>();
        services.AddSingleton<IGoogleDriveClient, GoogleDriveClient>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IBackupSchedulerService, BackupSchedulerService>();
        services.AddSingleton<ITorrentContentValidationService, QbittorrentTorrentContentValidationService>();
        services.AddSingleton<ITorrentCleanupService, TorrentCleanupService>();
        services.AddSingleton<ITorrentBlacklistService, TorrentBlacklistService>();
        services.AddSingleton<ITorrentAddGateService, TorrentAddGateService>();

        services.AddSingleton<IWorkspaceNavigator, WorkspaceNavigator>();
        services.AddSingleton<AutoTrackViewModel>();
        services.AddSingleton<NewsViewModel>();
        services.AddSingleton<FindAddViewModel>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<StatsViewModel>();
        services.AddSingleton<TorrentWorkspaceViewModel>();
        services.AddSingleton<RecipeWorkspaceViewModel>();
        services.AddSingleton<SystemSettingsViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }

    private void WarmupPosterCache()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var provider = _serviceProvider;
                if (provider is null)
                {
                    return;
                }

                var posterService = provider.GetRequiredService<IPosterImageService>();
                var database = provider.GetRequiredService<IDatabaseService>();
                foreach (var show in database.GetTrackedShows())
                {
                    await posterService.EnsureCachedAsync(
                        MediaKind.TvEpisode,
                        show.TmdbId,
                        show.PosterPath);
                }

                foreach (var movie in database.GetTrackedMovies())
                {
                    await posterService.EnsureCachedAsync(
                        MediaKind.Movie,
                        movie.TmdbId,
                        movie.PosterPath);
                }
            }
            catch
            {
            }
        });
    }

    private static void NormalizeGeminiSettings(ISettingsService settings, IGeminiModelCatalogService modelCatalog)
    {
        settings.Current.Gemini ??= new GeminiSettings();
        settings.Current.Gemini.Model = modelCatalog.Normalize(settings.Current.Gemini.Model);
        if (settings.Current.Gemini.FallbackModels.Length == 0)
        {
            settings.Current.Gemini.FallbackModels = modelCatalog.FallbackModels
                .Where(model => !string.Equals(model, settings.Current.Gemini.Model, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
    }

    private void StartCrashLoggingHooks()
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionEnding += OnSessionEnding;
    }

    private void StopCrashLoggingHooks()
    {
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionEnding -= OnSessionEnding;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            _serviceProvider?.GetService<ICrashLogService>()?.LogUnhandled(
                "UnhandledException",
                "AppDomain unhandled exception.",
                e.ExceptionObject as Exception,
                terminating: e.IsTerminating);
        }
        catch
        {
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            _serviceProvider?.GetService<ICrashLogService>()?.LogUnhandled(
                "DispatcherUnhandledException",
                "Dispatcher unhandled exception.",
                e.Exception);
        }
        catch
        {
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            _serviceProvider?.GetService<ICrashLogService>()?.LogUnhandled(
                "UnobservedTaskException",
                "Unobserved task exception.",
                e.Exception);
            e.SetObserved();
        }
        catch
        {
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        try
        {
            _serviceProvider?.GetService<IAppLogger>()?.Info(
                $"Power mode changed: {e.Mode}.",
                LogTarget.File | LogTarget.Console);
        }
        catch
        {
        }
    }

    private void OnSessionEnding(object sender, SessionEndingEventArgs e)
    {
        try
        {
            _serviceProvider?.GetService<IAppLogger>()?.Warning(
                $"Windows session ending: {e.Reason}.",
                LogTarget.File | LogTarget.Console);
        }
        catch
        {
        }
    }
}
