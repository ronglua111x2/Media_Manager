using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;
using media_management_app.Common;

namespace media_management_app.Services;

public sealed class WindowsStartupService : IWindowsStartupService
{
    private const string RunRegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsRegistered()
    {
        try
        {
            var result = RunSchtasks($"/query /tn \"{AppConstants.StartupTaskName}\"", throwOnError: false);
            return result == 0;
        }
        catch
        {
            return false;
        }
    }

    public void SetEnabled(bool enabled)
    {
        CleanupLegacyRegistryEntry();

        if (!enabled)
        {
            RunSchtasks($"/delete /tn \"{AppConstants.StartupTaskName}\" /f", throwOnError: false);
            return;
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("Could not determine the application executable path for Windows startup.");
        }

        var currentUser = WindowsIdentity.GetCurrent().Name;

        var args = $"/create /tn \"{AppConstants.StartupTaskName}\" " +
                   $"/tr \"\\\"{executablePath}\\\"\" " +
                   $"/sc ONLOGON /ru \"{currentUser}\" /rl HIGHEST /delay 0000:10 /f";

        RunSchtasks(args, throwOnError: true);
    }

    private static void CleanupLegacyRegistryEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKeyPath, writable: true);
            key?.DeleteValue(AppConstants.WindowsStartupRegistryValueName, throwOnMissingValue: false);
        }
        catch
        {
            // Legacy cleanup is best-effort; do not fail the main operation.
        }
    }

    private static int RunSchtasks(string arguments, bool throwOnError)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        };

        process.Start();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (throwOnError && process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"schtasks failed (exit {process.ExitCode}): {stderr.Trim()}");
        }

        return process.ExitCode;
    }
}
