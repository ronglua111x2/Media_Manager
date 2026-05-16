using System.Windows;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using media_management_app.Services;
using media_management_app.ViewModels;

namespace media_management_app;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        var settings = _serviceProvider.GetRequiredService<ISettingsService>();
        settings.Load();

        var database = _serviceProvider.GetRequiredService<IDatabaseService>();
        database.Initialize(settings.Current.StateFolder);

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder => builder.AddDebug());
        services.AddSingleton<HttpClient>();

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IAppLogger, AppLogger>();
        services.AddSingleton<IDatabaseService, DatabaseService>();
        services.AddSingleton<IParserService, ParserService>();
        services.AddSingleton<IScannerService, ScannerService>();
        services.AddSingleton<IHardlinkService, HardlinkService>();
        services.AddSingleton<TmdbMetadataProvider>();
        services.AddSingleton<IMetadataProvider>(provider => provider.GetRequiredService<TmdbMetadataProvider>());

        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<InboxViewModel>();
        services.AddSingleton<ReviewViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }
}
