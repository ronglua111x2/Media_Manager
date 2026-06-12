using System.Windows;
using System.Net.Http;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using media_management_app.Services;
using media_management_app.ViewModels;
using Wpf.Ui.Appearance;

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
        ApplicationThemeManager.Apply(ApplicationTheme.Light);
        ApplicationAccentColorManager.Apply(System.Windows.Media.Color.FromRgb(0x3d, 0x7d, 0x6b), ApplicationTheme.Light);

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        var settings = _serviceProvider.GetRequiredService<ISettingsService>();
        settings.Load();

        _serviceProvider.GetRequiredService<ILogCleanupService>().Start();

        var database = _serviceProvider.GetRequiredService<IDatabaseService>();
        database.Initialize(settings.Current.StateFolder);

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
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
        services.AddSingleton<IAppLogger, AppLogger>();
        services.AddSingleton<ILogCleanupService, LogCleanupService>();
        services.AddSingleton<IOperationProgressService, OperationProgressService>();
        services.AddSingleton<IDatabaseService, DatabaseService>();
        services.AddSingleton<IParserService, ParserService>();
        services.AddSingleton<IScannerService, ScannerService>();
        services.AddSingleton<ILibraryPathResolver, LibraryPathResolver>();
        services.AddSingleton<IHardlinkService, HardlinkService>();
        services.AddSingleton<ISourceReconciliationService, SourceReconciliationService>();
        services.AddSingleton<IQbittorrentClient, QbittorrentClient>();
        services.AddSingleton<IRecipeService, RecipeService>();
        services.AddSingleton<ISearchPlanBuilder, SearchPlanBuilder>();
        services.AddSingleton<ICandidateEvaluationService, CandidateEvaluationService>();
        services.AddSingleton<IAutomationFlowService, AutomationFlowService>();
        services.AddSingleton<IDeviceStatusService, DeviceStatusService>();
        services.AddSingleton<IDownloadFolderCatalogService, DownloadFolderCatalogService>();
        services.AddSingleton<ITorrentAddDiskAssignmentService, TorrentAddDiskAssignmentService>();
        services.AddSingleton<IConsoleWindowService, ConsoleWindowService>();
        services.AddSingleton<IQbittorrentWebViewHostService, QbittorrentWebViewHostService>();
        services.AddSingleton<ShowSearchSnapshotService>();
        services.AddSingleton<TmdbMetadataProvider>();
        services.AddSingleton<IMetadataProvider>(provider => provider.GetRequiredService<TmdbMetadataProvider>());
        services.AddSingleton<ITmdbShowCatalogService>(provider => provider.GetRequiredService<TmdbMetadataProvider>());
        services.AddSingleton<ITmdbMovieCatalogService>(provider => provider.GetRequiredService<TmdbMetadataProvider>());
        services.AddSingleton<ITrackedShowService, TrackedShowService>();
        services.AddSingleton<ITrackedMovieService, TrackedMovieService>();
        services.AddSingleton<IPosterImageService, PosterImageService>();
        services.AddSingleton<ITorrentCartService, TorrentCartService>();
        services.AddSingleton<IMediaCardCatalogService, MediaCardCatalogService>();
        services.AddSingleton<IMediaImportService, MediaImportService>();
        services.AddSingleton<IFetchJobService, FetchJobService>();
        services.AddSingleton<IAutoTorrentLinkService, AutoTorrentLinkService>();
        services.AddSingleton<ITorrentReconciliationService, TorrentReconciliationService>();
        services.AddSingleton<ILibraryManagementService, LibraryManagementService>();

        services.AddSingleton<FindAddViewModel>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<TorrentWorkspaceViewModel>();
        services.AddSingleton<QbittorrentWorkspaceViewModel>();
        services.AddSingleton<RecipeWorkspaceViewModel>();
        services.AddSingleton<SystemSettingsViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }
}
