using System.Text.Json;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class SettingsService : ISettingsService
{
    private const string SettingsFileName = "settings.json";
    private const string SettingsLoadLogFileName = "settings-load.log";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SettingsService()
    {
        Current = new AppSettings();
    }

    public AppSettings Current { get; private set; }

    public string SettingsFilePath => Path.Combine(Current.StateFolder, SettingsFileName);

    public void Load()
    {
        var initialStateFolder = Current.StateFolder;
        var initialSettingsFilePath = SettingsFilePath;
        WriteBootstrapLog(initialStateFolder, $"Starting settings load. InitialStateFolder='{initialStateFolder}', SettingsFilePath='{initialSettingsFilePath}'");

        Directory.CreateDirectory(Current.StateFolder);

        if (!File.Exists(initialSettingsFilePath))
        {
            WriteBootstrapLog(initialStateFolder, $"Settings file does not exist. Creating default settings at '{initialSettingsFilePath}'.");
            Save();
            return;
        }

        try
        {
            var json = File.ReadAllText(initialSettingsFilePath);
            WriteBootstrapLog(initialStateFolder, $"Read settings file. Length={json.Length} character(s).");

            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (loaded is null)
            {
                WriteBootstrapLog(initialStateFolder, "Settings JSON deserialized to null. Falling back to default settings.");
                Current = new AppSettings();
            }
            else
            {
                Current = loaded;
            }

            EnsureDefaults();
            WriteBootstrapLog(
                Current.StateFolder,
                $"Loaded settings. StateFolder='{Current.StateFolder}', SourceFolders={Current.SourceFolders.Count}, DefaultLibraryFolderName='{Current.DefaultLibraryFolderName}', TokenConfigured={!string.IsNullOrWhiteSpace(Current.TmdbReadAccessToken)}.");

            foreach (var folder in Current.SourceFolders)
            {
                WriteBootstrapLog(Current.StateFolder, $"Loaded source folder: {folder}");
            }

            if (!string.Equals(initialStateFolder, Current.StateFolder, StringComparison.OrdinalIgnoreCase))
            {
                WriteBootstrapLog(
                    Current.StateFolder,
                    $"Settings changed StateFolder from '{initialStateFolder}' to '{Current.StateFolder}'. Startup settings were read from '{initialSettingsFilePath}'.");
            }
        }
        catch (Exception ex)
        {
            WriteBootstrapLog(initialStateFolder, $"Failed to load settings from '{initialSettingsFilePath}'. {ex}");
            throw;
        }
    }

    public void Save()
    {
        EnsureDefaults();
        Directory.CreateDirectory(Current.StateFolder);
        var json = JsonSerializer.Serialize(Current, JsonOptions);
        File.WriteAllText(SettingsFilePath, json);
        WriteBootstrapLog(Current.StateFolder, $"Saved settings to '{SettingsFilePath}'. SourceFolders={Current.SourceFolders.Count}, TokenConfigured={!string.IsNullOrWhiteSpace(Current.TmdbReadAccessToken)}.");
    }

    private void EnsureDefaults()
    {
        if (string.IsNullOrWhiteSpace(Current.StateFolder))
        {
            Current.StateFolder = AppConstants.DefaultStateFolder;
        }

        if (string.IsNullOrWhiteSpace(Current.DefaultLibraryFolderName))
        {
            Current.DefaultLibraryFolderName = AppConstants.DefaultLibraryFolderName;
        }

        Current.AutoTorrent ??= new AutoTorrentSettings();
        Current.Warp ??= new WarpSettings();
        Current.Warp.ConnectTimeoutSeconds = Math.Clamp(Current.Warp.ConnectTimeoutSeconds, 5, 120);
        Current.AutoTrack ??= new AutoTrackSettings();
        MigrateAutoTrackSettings(Current.AutoTrack);
        Current.Logs ??= new LogSettings();
        Current.Startup ??= new AppStartupSettings();
        Current.Ui ??= new UiSettings();
        Current.Logs.MaxLinesPerFile = Math.Clamp(
            Current.Logs.MaxLinesPerFile,
            AppConstants.MinLogLinesPerFile,
            AppConstants.MaxConfigurableLogLinesPerFile);
        Current.Logs.CleanupRetentionDays = Math.Clamp(
            Current.Logs.CleanupRetentionDays,
            AppConstants.MinLogCleanupRetentionDays,
            AppConstants.MaxLogCleanupRetentionDays);

        if (string.IsNullOrWhiteSpace(Current.AutoTorrent.QbittorrentWebUiUrl))
        {
            Current.AutoTorrent.QbittorrentWebUiUrl = "http://localhost:8080";
        }
        if (string.IsNullOrWhiteSpace(Current.AutoTorrent.CategoryName))
        {
            Current.AutoTorrent.CategoryName = "AutoTorrent";
        }
        Current.AutoTorrent.DownloadFolders ??= [];
        Current.AutoTorrent.DownloadFolders = Current.AutoTorrent.DownloadFolders
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Select(folder => folder.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!string.IsNullOrWhiteSpace(Current.AutoTorrent.DownloadFolder) &&
            !Current.AutoTorrent.DownloadFolders.Contains(Current.AutoTorrent.DownloadFolder, StringComparer.OrdinalIgnoreCase))
        {
            Current.AutoTorrent.DownloadFolders.Insert(0, Current.AutoTorrent.DownloadFolder);
        }
        Current.AutoTorrent.MaxCandidatesPerFetch = Math.Clamp(Current.AutoTorrent.MaxCandidatesPerFetch, 1, 10);
        Current.AutoTorrent.MaxParallelSearches = Math.Clamp(Current.AutoTorrent.MaxParallelSearches, 1, 4);

        if (string.IsNullOrWhiteSpace(Current.OutputLibraryFolder))
        {
            Current.OutputLibraryFolder = null;
        }
        Current.LibraryRootMode = LibraryRootMode.AutoPerDrive;
        Current.SourceFolders = Current.SourceFolders
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Current.DriveLibraryRoots ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Current.Symlink ??= new SymlinkSettings();
        if (string.IsNullOrWhiteSpace(Current.Symlink.UnifiedRoot))
        {
            Current.Symlink.UnifiedRoot = AppConstants.DefaultSymlinkUnifiedRoot;
        }
    }

    private static void MigrateAutoTrackSettings(AutoTrackSettings autoTrack)
    {
        autoTrack.IntervalHours = Math.Clamp(autoTrack.IntervalHours, 1, 168);
        if (autoTrack.TorrentHuntIntervalMinutes <= 0)
        {
            autoTrack.TorrentHuntIntervalMinutes = Math.Clamp(autoTrack.IntervalHours * 60, 15, 1440);
        }

        autoTrack.TmdbCheckIntervalMinutes = Math.Clamp(autoTrack.TmdbCheckIntervalMinutes, 5, 1440);
        autoTrack.TorrentHuntIntervalMinutes = Math.Clamp(autoTrack.TorrentHuntIntervalMinutes, 15, 1440);
        autoTrack.ReconcileIntervalMinutes = Math.Clamp(autoTrack.ReconcileIntervalMinutes, 5, 1440);
        autoTrack.MaxTmdbRefreshesPerDay = Math.Clamp(autoTrack.MaxTmdbRefreshesPerDay, 1, 500);

        if (string.IsNullOrWhiteSpace(autoTrack.AnchorTimeLocal))
        {
            autoTrack.AnchorTimeLocal = "21:00";
        }

        autoTrack.Quality ??= new AutoTrackQualityPolicy();
        autoTrack.Search ??= new AutoTrackSearchSettings();
        autoTrack.Search.MaxShowsPerHuntCycle = Math.Clamp(autoTrack.Search.MaxShowsPerHuntCycle, 1, 20);
        autoTrack.Search.MaxParallelWorkersPerShow = Math.Clamp(autoTrack.Search.MaxParallelWorkersPerShow, 1, 4);
    }

    private static void WriteBootstrapLog(string stateFolder, string message)
    {
        var line = $"[{DateTime.Now.ToString(AppConstants.LogTimestampFormat)}] [SET] [SettingsService.cs] {message}";
        Console.WriteLine(line);
        System.Diagnostics.Debug.WriteLine(line);

        try
        {
            Directory.CreateDirectory(stateFolder);
            File.AppendAllText(Path.Combine(stateFolder, SettingsLoadLogFileName), line + Environment.NewLine);
        }
        catch
        {
            // Settings diagnostics must never prevent app startup.
        }
    }
}
