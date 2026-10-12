using System.ComponentModel;
using System.Diagnostics;
using media_management_app.Common;

namespace media_management_app.Services;

/// <summary>
/// Fire-and-forget shell launches with a per-destination cooldown so click-spam
/// does not spawn extra Explorer windows or browser tabs.
/// </summary>
public sealed class ShellLaunchGuard
{
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(2);

    private readonly IAppLogger _logger;
    private readonly object _gate = new();
    private readonly Dictionary<string, DateTime> _lastLaunchUtc = new(StringComparer.OrdinalIgnoreCase);

    public ShellLaunchGuard(IAppLogger logger)
    {
        _logger = logger;
    }

    public bool TryLaunch(string pathOrUrl, bool requireExistingDirectory = false)
    {
        var key = pathOrUrl.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        lock (_gate)
        {
            if (_lastLaunchUtc.TryGetValue(key, out var lastUtc) &&
                DateTime.UtcNow - lastUtc < Cooldown)
            {
                _logger.Debug($"Shell launch skipped (cooldown): {key}", LogTarget.File | LogTarget.Console);
                return false;
            }

            _lastLaunchUtc[key] = DateTime.UtcNow;
        }

        if (requireExistingDirectory && !Directory.Exists(key))
        {
            _logger.Debug($"Shell launch skipped (folder missing): {key}", LogTarget.File | LogTarget.Console);
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = key,
                UseShellExecute = true
            });
            _logger.Info($"Opened {key}", LogTarget.All);
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            _logger.Debug($"Shell launch failed for '{key}': {ex.Message}", LogTarget.File | LogTarget.Console);
            return false;
        }
    }
}
