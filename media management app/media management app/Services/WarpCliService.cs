using System.Diagnostics;
using System.Text;
using System.Text.Json;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class WarpCliService : IWarpCliService
{
    private const int StatusPollIntervalMilliseconds = 2000;

    private readonly ISettingsService _settingsService;
    private readonly IWindowsNotificationService _windowsNotificationService;
    private readonly IAppLogger _logger;

    public WarpCliService(
        ISettingsService settingsService,
        IWindowsNotificationService windowsNotificationService,
        IAppLogger logger)
    {
        _settingsService = settingsService;
        _windowsNotificationService = windowsNotificationService;
        _logger = logger;
    }

    public string ResolvedExecutablePath
    {
        get
        {
            var configured = _settingsService.Current.Warp?.ExecutablePath?.Trim();
            return string.IsNullOrWhiteSpace(configured)
                ? AppConstants.DefaultWarpCliPath
                : configured;
        }
    }

    public bool IsAvailable => File.Exists(ResolvedExecutablePath);

    public async Task<bool> ConnectAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var attempt = await ConnectOwnedAsync(timeout, ct);
        return attempt.Connected;
    }

    public async Task<WarpConnectAttempt> ConnectOwnedAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            _logger.Warning($"WARP CLI not available at {ResolvedExecutablePath}.", LogTarget.All);
            return new WarpConnectAttempt(false, false);
        }

        try
        {
            if (await IsConnectedAsync(ct))
            {
                _logger.Info("WARP is already connected.", LogTarget.File | LogTarget.Console);
                return new WarpConnectAttempt(true, false);
            }

            _logger.Info("Connecting WARP via warp-cli...", LogTarget.All);
            await RunCliAsync("connect", ct);

            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();

                if (await IsConnectedAsync(ct))
                {
                    _logger.Info("WARP connected.", LogTarget.All);
                    _windowsNotificationService.TryShow(new WindowsNotificationRequest
                    {
                        Title = "WARP",
                        Message = "Connected.",
                        Kind = NotificationKind.WarpRecovered
                    });
                    return new WarpConnectAttempt(true, true);
                }

                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                await Task.Delay(
                    TimeSpan.FromMilliseconds(Math.Min(StatusPollIntervalMilliseconds, remaining.TotalMilliseconds)),
                    ct);
            }

            _logger.Warning($"WARP connect timed out after {timeout.TotalSeconds:0}s.", LogTarget.All);
            return new WarpConnectAttempt(false, false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning($"WARP connect failed: {ex.Message}", LogTarget.All);
            return new WarpConnectAttempt(false, false);
        }
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            _logger.Warning($"WARP CLI not available at {ResolvedExecutablePath}.", LogTarget.All);
            return;
        }

        try
        {
            _logger.Info("Disconnecting WARP via warp-cli...", LogTarget.All);
            var result = await RunCliAsync("disconnect", ct);
            _logger.Info(
                $"WARP disconnect completed (exit {result.ExitCode}). Output: {result.Output.Trim()}",
                LogTarget.File | LogTarget.Console);
            _windowsNotificationService.TryShow(new WindowsNotificationRequest
            {
                Title = "WARP",
                Message = "Disconnected.",
                Kind = NotificationKind.WarpDisconnected
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning($"WARP disconnect failed: {ex.Message}", LogTarget.All);
        }
    }

    public async Task<bool> IsConnectedAsync(CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            return false;
        }

        try
        {
            var jsonResult = await RunCliAsync("status --json", ct);
            if (ParseConnectedStatus(jsonResult.Output))
            {
                return true;
            }

            var textResult = await RunCliAsync("status", ct);
            return ParseConnectedStatus(textResult.Output);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning($"WARP status check failed: {ex.Message}", LogTarget.File | LogTarget.Console);
            return false;
        }
    }

    private async Task<CliRunResult> RunCliAsync(string arguments, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ResolvedExecutablePath,
            Arguments = $"--accept-tos {arguments}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = startInfo };
        var outputBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                outputBuilder.AppendLine(e.Data);
                _logger.Debug($"[warp-cli] {e.Data}", LogTarget.File | LogTarget.Console);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                outputBuilder.AppendLine(e.Data);
                _logger.Debug($"[warp-cli stderr] {e.Data}", LogTarget.File | LogTarget.Console);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(ct);

        return new CliRunResult(process.ExitCode, outputBuilder.ToString());
    }

    private static bool ParseConnectedStatus(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        if (output.Contains("Disconnected", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (output.Contains("Connected", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(output);
            return ContainsConnectedStatus(document.RootElement);
        }
        catch (JsonException)
        {
            return output.Contains("Connected", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool ContainsConnectedStatus(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return string.Equals(element.GetString(), "Connected", StringComparison.OrdinalIgnoreCase);
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (ContainsConnectedStatus(property.Value))
                    {
                        return true;
                    }
                }

                return false;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (ContainsConnectedStatus(item))
                    {
                        return true;
                    }
                }

                return false;
            default:
                return false;
        }
    }

    private sealed record CliRunResult(int ExitCode, string Output);
}
