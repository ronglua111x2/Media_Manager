using Microsoft.Win32;
using media_management_app.Common;

namespace media_management_app.Services;

public sealed class WindowsStartupService : IWindowsStartupService
{
    private const string RunRegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKeyPath, writable: false);
        var value = key?.GetValue(AppConstants.WindowsStartupRegistryValueName) as string;
        return !string.IsNullOrWhiteSpace(value);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunRegistryKeyPath, writable: true);

        if (!enabled)
        {
            key.DeleteValue(AppConstants.WindowsStartupRegistryValueName, throwOnMissingValue: false);
            return;
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("Could not determine the application executable path for Windows startup.");
        }

        key.SetValue(AppConstants.WindowsStartupRegistryValueName, $"\"{executablePath}\"");
    }
}
