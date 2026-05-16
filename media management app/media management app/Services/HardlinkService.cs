using System.Runtime.InteropServices;
using System.ComponentModel;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class HardlinkService : IHardlinkService
{
    private readonly IAppLogger _logger;

    public HardlinkService(IAppLogger logger)
    {
        _logger = logger;
    }

    public string BuildOutputPath(SourceItem item, string outputRoot)
    {
        var showName = Sanitize(item.ShowTitle ?? "Unknown Show");
        var seasonFolder = $"Season {item.SeasonNumber.GetValueOrDefault():00}";
        var fileName = $"{showName} - S{item.SeasonNumber.GetValueOrDefault():00}E{item.EpisodeNumber.GetValueOrDefault():00}{Path.GetExtension(item.FilePath)}";
        return Path.Combine(outputRoot, showName, seasonFolder, fileName);
    }

    public bool CreateHardLink(SourceItem item, string outputRoot, out string? createdPath, out string? errorMessage)
    {
        createdPath = null;
        errorMessage = null;

        var targetPath = BuildOutputPath(item, outputRoot);
        if (File.Exists(targetPath))
        {
            errorMessage = "Output already exists.";
            _logger.Warning($"Hardlink skipped because output exists: {targetPath}", LogTarget.All);
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        _logger.Info($"Creating hardlink from {item.FilePath} to {targetPath}", LogTarget.File | LogTarget.Console);
        var ok = CreateHardLinkNative(targetPath, item.FilePath, IntPtr.Zero);
        if (!ok)
        {
            var errorCode = Marshal.GetLastWin32Error();
            errorMessage = new Win32Exception(errorCode).Message;
            _logger.Error($"Hardlink failed: {errorMessage}", targets: LogTarget.All);
            return false;
        }

        createdPath = targetPath;
        _logger.Info($"Created hardlink: {targetPath}", LogTarget.All);
        return true;
    }

    private static string Sanitize(string value)
    {
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(ch, '_');
        }

        return string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateHardLinkNative(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
}
