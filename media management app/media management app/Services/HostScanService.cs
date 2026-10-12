using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services.Backup;
using media_management_app.Services.Gemini;

namespace media_management_app.Services;

public sealed class HostScanService : IHostScanService
{
    private readonly ISettingsService _settingsService;
    private readonly IQbittorrentClient _qbittorrentClient;
    private readonly IJellyfinClient _jellyfinClient;
    private readonly IWarpCliService _warpCliService;
    private readonly IGeminiApiClient _geminiApiClient;
    private readonly IGoogleDriveClient _googleDriveClient;
    private readonly IAppLogger _logger;
    private readonly HttpClient _httpClient;

    public HostScanService(
        ISettingsService settingsService,
        IQbittorrentClient qbittorrentClient,
        IJellyfinClient jellyfinClient,
        IWarpCliService warpCliService,
        IGeminiApiClient geminiApiClient,
        IGoogleDriveClient googleDriveClient,
        IAppLogger logger,
        HttpClient httpClient)
    {
        _settingsService = settingsService;
        _qbittorrentClient = qbittorrentClient;
        _jellyfinClient = jellyfinClient;
        _warpCliService = warpCliService;
        _geminiApiClient = geminiApiClient;
        _googleDriveClient = googleDriveClient;
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<HostScanRow>> ScanAsync(
        HostScanOverrides? overrides = null,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<HostScanRow>
        {
            ScanWindows(),
            ScanApp(),
            ScanWebView2()
        };
        rows.Add(await ScanQbittorrentAsync(overrides, cancellationToken).ConfigureAwait(false));
        rows.Add(await ScanWarpAsync(overrides, cancellationToken).ConfigureAwait(false));
        rows.Add(await ScanJellyfinAsync(cancellationToken).ConfigureAwait(false));
        rows.Add(await ScanTmdbAsync(cancellationToken).ConfigureAwait(false));
        rows.Add(await ScanGeminiAsync(cancellationToken).ConfigureAwait(false));
        rows.Add(await ScanDriveAsync(cancellationToken).ConfigureAwait(false));

        foreach (var row in rows)
        {
            _logger.Info(row.LogLine, LogTarget.All);
        }

        return rows;
    }

    private static HostScanRow ScanWindows()
    {
        var version = Environment.OSVersion.Version;
        var detected = version.ToString();
        var ok = version >= HostSoftwareCatalog.MinWindowsVersion;
        return new HostScanRow
        {
            Id = HostScanComponentId.Windows,
            Title = "Windows",
            RequiredLabel = HostSoftwareCatalog.WindowsRequiredLabel,
            DetectedVersion = detected,
            Installed = HostScanCheckStatus.Ok,
            Running = HostScanCheckStatus.NotApplicable,
            Usable = ok ? HostScanCheckStatus.Ok : HostScanCheckStatus.Failed,
            Severity = ok ? HostScanSeverity.Ok : HostScanSeverity.Error,
            Note = ok ? "Windows version is new enough." : $"Need {HostSoftwareCatalog.WindowsRequiredLabel}."
        };
    }

    private static HostScanRow ScanApp()
    {
        var path = Environment.ProcessPath;
        var version = TryFileVersion(path);
        return new HostScanRow
        {
            Id = HostScanComponentId.App,
            Title = "Media Manager",
            Path = path,
            RequiredLabel = HostSoftwareCatalog.AppRequiredLabel,
            DetectedVersion = version,
            Installed = HostScanCheckStatus.Ok,
            Running = HostScanCheckStatus.Ok,
            Usable = HostScanCheckStatus.Ok,
            Severity = HostScanSeverity.Ok,
            Note = "This app is running."
        };
    }

    private static HostScanRow ScanWebView2()
    {
        string? version = null;
        try
        {
            version = CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            version = null;
        }
        catch (Exception)
        {
            version = null;
        }

        var found = !string.IsNullOrWhiteSpace(version);
        return new HostScanRow
        {
            Id = HostScanComponentId.WebView2,
            Title = "WebView2",
            RequiredLabel = HostSoftwareCatalog.WebView2RequiredLabel,
            DetectedVersion = version,
            Installed = found ? HostScanCheckStatus.Ok : HostScanCheckStatus.Missing,
            Running = HostScanCheckStatus.NotApplicable,
            Usable = found ? HostScanCheckStatus.Ok : HostScanCheckStatus.Missing,
            Severity = found ? HostScanSeverity.Ok : HostScanSeverity.Warning,
            Note = found
                ? "Needed for qBittorrent and Jellyfin viewers."
                : "Install the Evergreen WebView2 Runtime."
        };
    }

    private async Task<HostScanRow> ScanQbittorrentAsync(
        HostScanOverrides? overrides,
        CancellationToken cancellationToken)
    {
        var defaultPath = QbittorrentProcessRestartSettings.DefaultExecutablePath;
        var path = FirstExistingFile(
            overrides?.QbittorrentExecutablePath,
            defaultPath,
            FindUninstallExe("qBittorrent", "qbittorrent.exe"));
        var installed = File.Exists(path);
        var detected = installed ? TryFileVersion(path) : null;
        var running = ProcessIsRunning(HostSoftwareCatalog.QbittorrentProcessName);
        var versionWarn = installed && !IsAtLeast(detected, HostSoftwareCatalog.MinQbittorrentVersion);

        var settings = _settingsService.Current.AutoTorrent ?? new AutoTorrentSettings();
        var webUiUrl = string.IsNullOrWhiteSpace(settings.QbittorrentWebUiUrl)
            ? HostSoftwareCatalog.QbittorrentDefaultWebUiUrl
            : settings.QbittorrentWebUiUrl;
        var portOpen = TryIsPortOpen(webUiUrl);
        var hasCreds = HasQbittorrentCredentials(settings);

        var usable = HostScanCheckStatus.NotConfigured;
        var note = HostSoftwareCatalog.NotConfiguredNote;
        if (hasCreds)
        {
            try
            {
                var webVersion = await _qbittorrentClient.TestConnectionAsync(cancellationToken).ConfigureAwait(false);
                usable = HostScanCheckStatus.Ok;
                if (!string.IsNullOrWhiteSpace(webVersion))
                {
                    detected = string.IsNullOrWhiteSpace(detected) ? webVersion : $"{detected} (WebUI {webVersion})";
                }

                note = "WebUI login succeeded.";
            }
            catch (Exception ex)
            {
                usable = HostScanCheckStatus.Failed;
                note = ex.Message;
            }
        }
        else if (portOpen)
        {
            note = "WebUI is open; credentials are not in this folder.";
        }
        else if (!installed)
        {
            note = "Browse to qbittorrent.exe if it is not in Program Files.";
        }
        else if (!running)
        {
            note = "Installed but not running. Start it yourself.";
        }

        var severity = !installed || versionWarn
            ? HostScanSeverity.Warning
            : usable == HostScanCheckStatus.Failed
                ? HostScanSeverity.Warning
                : HostScanSeverity.Ok;

        return new HostScanRow
        {
            Id = HostScanComponentId.Qbittorrent,
            Title = "qBittorrent",
            CanBrowse = true,
            Path = path,
            DetectedVersion = detected,
            RequiredLabel = HostSoftwareCatalog.QbittorrentRequiredLabel,
            Installed = installed ? HostScanCheckStatus.Ok : HostScanCheckStatus.Missing,
            Running = running ? HostScanCheckStatus.Ok : HostScanCheckStatus.Missing,
            Usable = usable,
            Severity = severity,
            Note = versionWarn
                ? $"Detected version looks below 5.1. {note}"
                : note
        };
    }

    private async Task<HostScanRow> ScanWarpAsync(
        HostScanOverrides? overrides,
        CancellationToken cancellationToken)
    {
        var defaultPath = AppConstants.DefaultWarpCliPath;
        var path = FirstExistingFile(
            overrides?.WarpCliPath,
            _warpCliService.ResolvedExecutablePath,
            defaultPath,
            FindUninstallExe("Cloudflare WARP", "warp-cli.exe"));
        if (string.IsNullOrWhiteSpace(path))
        {
            path = defaultPath;
        }

        var installed = File.Exists(path);
        var running = AnyProcessRunning(HostSoftwareCatalog.WarpProcessNames);
        string? detected = null;
        if (installed)
        {
            detected = await TryRunVersionAsync(path, "--version", cancellationToken).ConfigureAwait(false)
                       ?? TryFileVersion(path);
        }

        return new HostScanRow
        {
            Id = HostScanComponentId.Warp,
            Title = "Cloudflare WARP",
            CanBrowse = true,
            Path = path,
            DetectedVersion = detected,
            RequiredLabel = HostSoftwareCatalog.WarpRequiredLabel,
            Installed = installed ? HostScanCheckStatus.Ok : HostScanCheckStatus.Missing,
            Running = running ? HostScanCheckStatus.Ok : HostScanCheckStatus.Missing,
            Usable = installed ? HostScanCheckStatus.Ok : HostScanCheckStatus.Missing,
            Severity = installed ? HostScanSeverity.Ok : HostScanSeverity.Info,
            Note = installed
                ? (running ? "warp-cli found." : "CLI found; WARP may not be running.")
                : "Optional. Browse if it is installed elsewhere."
        };
    }

    private async Task<HostScanRow> ScanJellyfinAsync(CancellationToken cancellationToken)
    {
        var settings = _settingsService.Current.AutoTrack?.Jellyfin ?? new JellyfinRefreshSettings();
        var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl)
            ? HostSoftwareCatalog.JellyfinDefaultBaseUrl
            : settings.BaseUrl;
        var running = AnyProcessRunning(HostSoftwareCatalog.JellyfinProcessNames);
        var portOpen = TryIsPortOpen(baseUrl);
        var hasKey = !string.IsNullOrWhiteSpace(settings.ApiKey);
        var usable = HostScanCheckStatus.NotConfigured;
        var note = HostSoftwareCatalog.NotConfiguredNote;
        string? detected = null;

        if (hasKey)
        {
            try
            {
                var summary = await _jellyfinClient.TestConnectionAsync(cancellationToken).ConfigureAwait(false);
                usable = HostScanCheckStatus.Ok;
                detected = summary;
                note = "System/Info succeeded.";
            }
            catch (Exception ex)
            {
                usable = HostScanCheckStatus.Failed;
                note = ex.Message;
            }
        }
        else if (portOpen || running)
        {
            note = "Local Jellyfin found; API key is not in this folder.";
        }
        else
        {
            note = "Optional. No local server; a remote URL is OK.";
        }

        var installed = running || portOpen ? HostScanCheckStatus.Ok : HostScanCheckStatus.Missing;
        var severity = usable == HostScanCheckStatus.Failed
            ? HostScanSeverity.Warning
            : usable == HostScanCheckStatus.Ok
                ? HostScanSeverity.Ok
                : HostScanSeverity.Info;
        return new HostScanRow
        {
            Id = HostScanComponentId.Jellyfin,
            Title = "Jellyfin",
            Path = baseUrl,
            DetectedVersion = detected,
            RequiredLabel = HostSoftwareCatalog.JellyfinRequiredLabel,
            Installed = installed,
            Running = running || portOpen ? HostScanCheckStatus.Ok : HostScanCheckStatus.Missing,
            Usable = usable,
            Severity = severity,
            Note = note
        };
    }

    private async Task<HostScanRow> ScanTmdbAsync(CancellationToken cancellationToken)
    {
        var token = _settingsService.Current.TmdbReadAccessToken?.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return TokenRow(
                HostScanComponentId.Tmdb,
                "TMDB",
                HostSoftwareCatalog.TmdbRequiredLabel,
                HostScanCheckStatus.NotConfigured,
                HostSoftwareCatalog.NotConfiguredNote);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.themoviedb.org/3/authentication");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return TokenRow(
                    HostScanComponentId.Tmdb,
                    "TMDB",
                    HostSoftwareCatalog.TmdbRequiredLabel,
                    HostScanCheckStatus.Ok,
                    "Token accepted.");
            }

            return TokenRow(
                HostScanComponentId.Tmdb,
                "TMDB",
                HostSoftwareCatalog.TmdbRequiredLabel,
                HostScanCheckStatus.Failed,
                $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
        }
        catch (Exception ex)
        {
            return TokenRow(
                HostScanComponentId.Tmdb,
                "TMDB",
                HostSoftwareCatalog.TmdbRequiredLabel,
                HostScanCheckStatus.Failed,
                ex.Message);
        }
    }

    private async Task<HostScanRow> ScanGeminiAsync(CancellationToken cancellationToken)
    {
        var key = _settingsService.Current.Gemini?.ApiKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            return TokenRow(
                HostScanComponentId.Gemini,
                "Gemini",
                HostSoftwareCatalog.GeminiRequiredLabel,
                HostScanCheckStatus.NotConfigured,
                "Optional. " + HostSoftwareCatalog.NotConfiguredNote);
        }

        try
        {
            var model = await _geminiApiClient.TestConnectionAsync(cancellationToken).ConfigureAwait(false);
            return TokenRow(
                HostScanComponentId.Gemini,
                "Gemini",
                HostSoftwareCatalog.GeminiRequiredLabel,
                HostScanCheckStatus.Ok,
                $"Connected ({model}).");
        }
        catch (Exception ex)
        {
            return TokenRow(
                HostScanComponentId.Gemini,
                "Gemini",
                HostSoftwareCatalog.GeminiRequiredLabel,
                HostScanCheckStatus.Failed,
                ex.Message);
        }
    }

    private async Task<HostScanRow> ScanDriveAsync(CancellationToken cancellationToken)
    {
        var credentialsExist = File.Exists(_googleDriveClient.CredentialsFilePath);
        if (!credentialsExist && !_googleDriveClient.HasStoredCredential)
        {
            return new HostScanRow
            {
                Id = HostScanComponentId.Drive,
                Title = "Google Drive",
                Path = _googleDriveClient.CredentialsFilePath,
                RequiredLabel = HostSoftwareCatalog.DriveRequiredLabel,
                Installed = HostScanCheckStatus.Missing,
                Running = HostScanCheckStatus.NotApplicable,
                Usable = HostScanCheckStatus.NotConfigured,
                Severity = HostScanSeverity.Info,
                Note = "Optional. " + HostSoftwareCatalog.NotConfiguredNote
            };
        }

        var silent = await _googleDriveClient.TryEnsureConnectedSilentlyAsync(cancellationToken).ConfigureAwait(false);
        return new HostScanRow
        {
            Id = HostScanComponentId.Drive,
            Title = "Google Drive",
            Path = _googleDriveClient.CredentialsFilePath,
            RequiredLabel = HostSoftwareCatalog.DriveRequiredLabel,
            Installed = credentialsExist ? HostScanCheckStatus.Ok : HostScanCheckStatus.Missing,
            Running = HostScanCheckStatus.NotApplicable,
            Usable = silent ? HostScanCheckStatus.Ok : HostScanCheckStatus.Failed,
            Severity = silent ? HostScanSeverity.Ok : HostScanSeverity.Warning,
            Note = silent ? "Silent token refresh succeeded." : "Credentials or token present but silent connect failed."
        };
    }

    private static HostScanRow TokenRow(
        HostScanComponentId id,
        string title,
        string required,
        HostScanCheckStatus usable,
        string note) =>
        new()
        {
            Id = id,
            Title = title,
            RequiredLabel = required,
            Installed = HostScanCheckStatus.NotApplicable,
            Running = HostScanCheckStatus.NotApplicable,
            Usable = usable,
            Severity = usable switch
            {
                HostScanCheckStatus.Ok => HostScanSeverity.Ok,
                HostScanCheckStatus.Failed => HostScanSeverity.Warning,
                _ => HostScanSeverity.Info
            },
            Note = note
        };

    private static bool HasQbittorrentCredentials(AutoTorrentSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.Username) ||
        !string.IsNullOrWhiteSpace(settings.Password) ||
        !string.IsNullOrWhiteSpace(settings.ApiKey);

    private static string? FirstExistingFile(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
    }

    private static string? FindUninstallExe(string displayNameContains, string exeFileName)
    {
        foreach (var root in new[]
                 {
                     @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                     @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
                 })
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(root);
                if (key is null)
                {
                    continue;
                }

                foreach (var subName in key.GetSubKeyNames())
                {
                    using var sub = key.OpenSubKey(subName);
                    var display = sub?.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(display) ||
                        display.IndexOf(displayNameContains, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    var location = sub?.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrWhiteSpace(location))
                    {
                        var exe = Path.Combine(location, exeFileName);
                        if (File.Exists(exe))
                        {
                            return exe;
                        }
                    }

                    var icon = sub?.GetValue("DisplayIcon") as string;
                    if (!string.IsNullOrWhiteSpace(icon))
                    {
                        var trimmed = icon.Split(',')[0].Trim().Trim('"');
                        if (File.Exists(trimmed) &&
                            trimmed.EndsWith(exeFileName, StringComparison.OrdinalIgnoreCase))
                        {
                            return trimmed;
                        }
                    }
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static bool ProcessIsRunning(string processName)
    {
        Process[]? processes = null;
        try
        {
            processes = Process.GetProcessesByName(processName);
            return processes.Length > 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (processes is not null)
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }
    }

    private static bool AnyProcessRunning(IEnumerable<string> names) =>
        names.Any(ProcessIsRunning);

    private static bool TryIsPortOpen(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        try
        {
            using var client = new TcpClient();
            var task = client.ConnectAsync(uri.Host, uri.Port);
            return task.Wait(TimeSpan.FromMilliseconds(400)) && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static string? TryFileVersion(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return info.ProductVersion ?? info.FileVersion;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsAtLeast(string? versionText, Version minimum)
    {
        if (string.IsNullOrWhiteSpace(versionText))
        {
            return true;
        }

        var cleaned = versionText.Trim().TrimStart('v', 'V');
        var prefix = new string(cleaned.TakeWhile(ch => char.IsDigit(ch) || ch is '.').ToArray());
        if (!Version.TryParse(prefix, out var parsed) &&
            !Version.TryParse(prefix + ".0", out parsed))
        {
            return true;
        }

        var comparable = new Version(
            parsed.Major,
            Math.Max(parsed.Minor, 0),
            Math.Max(parsed.Build, 0));
        return comparable >= minimum;
    }

    private static async Task<string?> TryRunVersionAsync(
        string exePath,
        string arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var text = string.IsNullOrWhiteSpace(output) ? error : output;
            var line = text.Trim().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return string.IsNullOrWhiteSpace(line) ? null : line.Trim();
        }
        catch
        {
            return null;
        }
    }
}
