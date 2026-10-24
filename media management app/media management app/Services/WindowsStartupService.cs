using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;
using Microsoft.Win32;
using media_management_app.Common;

namespace media_management_app.Services;

public sealed class WindowsStartupService : IWindowsStartupService
{
    private const string RunRegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static readonly XNamespace TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private readonly IAppLogger _logger;

    public WindowsStartupService(IAppLogger logger)
    {
        _logger = logger;
    }

    public bool IsRegistered()
    {
        try
        {
            var result = RunSchtasks($"/query /tn \"{AppConstants.StartupTaskName}\"", throwOnError: false);
            return result.ExitCode == 0;
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

        var executablePath = GetExecutablePath();
        var registered = TryReadRegisteredTask();
        if (IsDevBuildPath(executablePath) &&
            registered?.Command is { } existing &&
            !PathsEqual(existing, executablePath) &&
            File.Exists(existing))
        {
            // Saving settings from a dev build used to repoint the logon task at bin\, so autostart
            // launched the dev build instead of the installed app.
            _logger.Warning(
                $"Windows startup task kept on '{existing}'. This is a dev build ('{executablePath}'), so it does not take over the task.",
                LogTarget.All);
            return;
        }

        Register(executablePath);
    }

    public void EnsureRegistered()
    {
        var executablePath = GetExecutablePath();
        var registered = TryReadRegisteredTask();
        var reason = GetReregisterReason(registered, executablePath);
        if (reason is null)
        {
            return;
        }

        if (IsDevBuildPath(executablePath) &&
            registered?.Command is { } existing &&
            !PathsEqual(existing, executablePath) &&
            File.Exists(existing))
        {
            _logger.Warning(
                $"Windows startup task points at '{existing}', not this dev build. Left unchanged.",
                LogTarget.All);
            return;
        }

        Register(executablePath);
        _logger.Info($"Windows startup task re-registered: {reason}. Exe='{executablePath}'.", LogTarget.All);
    }

    public static string? GetReregisterReason(RegisteredStartupTask? registered, string executablePath)
    {
        if (registered is null)
        {
            return "task was missing";
        }

        if (registered.Command is null || !PathsEqual(registered.Command, executablePath))
        {
            return $"task pointed at '{registered.Command ?? "(none)"}'";
        }

        if (registered.DisallowStartIfOnBatteries || registered.StopIfGoingOnBatteries)
        {
            return "task only ran on AC power and was stopped when switching to battery";
        }

        if (registered.ExecutionTimeLimit is not ("PT0S" or null))
        {
            return $"task had a run time limit of {registered.ExecutionTimeLimit}";
        }

        return null;
    }

    public static bool IsDevBuildPath(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var binIndex = Array.FindIndex(segments, segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase));
        return binIndex >= 0 && segments
            .Skip(binIndex + 1)
            .Any(segment => segment.StartsWith("net", StringComparison.OrdinalIgnoreCase));
    }

    public static string BuildTaskXml(string executablePath, string userName, DateTime now)
    {
        var command = SecurityElement.Escape(executablePath);
        var workingDirectory = SecurityElement.Escape(Path.GetDirectoryName(executablePath) ?? string.Empty);
        var user = SecurityElement.Escape(userName);

        // schtasks /create without /xml always sets "Start only on AC power", "Stop if the computer
        // switches to battery power", a 72 h limit, and below-normal priority. Battery stop ended the
        // app on every AC unplug (docs/debug/unclean-exit).
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Date>{now:yyyy-MM-ddTHH:mm:ss}</Date>
                <Author>{user}</Author>
                <Description>Starts Media Manager at logon.</Description>
                <URI>\{SecurityElement.Escape(AppConstants.StartupTaskName)}</URI>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{user}</UserId>
                  <Delay>PT10S</Delay>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{command}</Command>
                  <WorkingDirectory>{workingDirectory}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    public static RegisteredStartupTask ParseTaskXml(string xml)
    {
        var document = XDocument.Parse(xml);
        var settings = document.Root?.Element(TaskNamespace + "Settings");
        var command = document.Root?
            .Element(TaskNamespace + "Actions")?
            .Element(TaskNamespace + "Exec")?
            .Element(TaskNamespace + "Command")?
            .Value
            .Trim()
            .Trim('"');

        return new RegisteredStartupTask(
            string.IsNullOrWhiteSpace(command) ? null : command,
            ReadBool(settings, "DisallowStartIfOnBatteries", defaultValue: true),
            ReadBool(settings, "StopIfGoingOnBatteries", defaultValue: true),
            settings?.Element(TaskNamespace + "ExecutionTimeLimit")?.Value.Trim() ?? "PT72H");
    }

    private void Register(string executablePath)
    {
        var userName = WindowsIdentity.GetCurrent().Name;
        var xmlPath = Path.Combine(Path.GetTempPath(), $"MediaManager-AutoStart-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(xmlPath, BuildTaskXml(executablePath, userName, DateTime.Now), Encoding.Unicode);
            RunSchtasks(
                $"/create /tn \"{AppConstants.StartupTaskName}\" /xml \"{xmlPath}\" /f",
                throwOnError: true);
        }
        finally
        {
            try
            {
                File.Delete(xmlPath);
            }
            catch
            {
            }
        }
    }

    private static RegisteredStartupTask? TryReadRegisteredTask()
    {
        try
        {
            var result = RunSchtasks($"/query /tn \"{AppConstants.StartupTaskName}\" /xml", throwOnError: false);
            return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output)
                ? ParseTaskXml(result.Output)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool ReadBool(XElement? settings, string name, bool defaultValue)
    {
        var value = settings?.Element(TaskNamespace + name)?.Value.Trim();
        return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private static string GetExecutablePath()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("Could not determine the application executable path for Windows startup.");
        }

        return executablePath;
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
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

    private static (int ExitCode, string Output) RunSchtasks(string arguments, bool throwOnError)
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
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        var stdout = stdoutTask.GetAwaiter().GetResult();

        if (throwOnError && process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"schtasks failed (exit {process.ExitCode}): {stderr.Trim()}");
        }

        return (process.ExitCode, stdout);
    }
}

public sealed record RegisteredStartupTask(
    string? Command,
    bool DisallowStartIfOnBatteries,
    bool StopIfGoingOnBatteries,
    string? ExecutionTimeLimit);
