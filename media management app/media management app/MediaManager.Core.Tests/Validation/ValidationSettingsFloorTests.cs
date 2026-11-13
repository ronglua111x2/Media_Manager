using FluentAssertions;
using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.Core.Tests.Validation;

/// <summary>
/// SEC-A09: settings.json stores the DangerousExtensions list, so an old, hand-edited or emptied list silently turned
/// off blocking of .exe, .bat, .ps1... The built-in list is now a floor that settings can only add to, and a weakened
/// list or a disabled check is logged as a warning.
/// </summary>
public class ValidationSettingsFloorTests
{
    private static TorrentContentFile File(string name, long size) => new() { Name = name, Size = size };

    [Theory]
    [InlineData("Setup.exe")]
    [InlineData("Install.bat")]
    [InlineData("Run.cmd")]
    [InlineData("x.ps1")]
    [InlineData("x.vbs")]
    [InlineData("x.js")]
    [InlineData("Codec.msi")]
    [InlineData("Player.scr")]
    public async Task SavedListWithoutBuiltIns_StillRejectsExecutables(string executable)
    {
        var config = new TorrentValidationConfig { DangerousExtensions = [".scr"] };
        if (executable.EndsWith(".scr", StringComparison.Ordinal))
        {
            config.DangerousExtensions = [];
        }

        var result = await new TorrentContentValidationService(() => config).ValidateFilesAsync(
            "abc",
            [File("Show.S01E01.1080p.mkv", 1_000_000_000), File(executable, 3_000_000)]);

        result.IsValid.Should().BeFalse();
        result.SuspiciousFiles.Should().Contain(file => file.FileName == executable && file.SuspicionLevel == SuspicionLevel.Critical);
    }

    [Fact]
    public async Task SavedListMissingBuiltIns_IsLoggedOnce()
    {
        var logger = new RecordingLogger();
        var config = new TorrentValidationConfig { DangerousExtensions = [".exe"] };
        var service = new TorrentContentValidationService(() => config, logger);

        await service.ValidateFilesAsync("a", [File("Show.S01E01.mkv", 1_000_000_000)]);
        await service.ValidateFilesAsync("b", [File("Show.S01E02.mkv", 1_000_000_000)]);

        logger.Warnings.Should().ContainSingle(message => message.Contains("DangerousExtensions") && message.Contains(".bat"));
    }

    [Fact]
    public async Task DefaultList_LogsNoSettingsWarning()
    {
        var logger = new RecordingLogger();
        var service = new TorrentContentValidationService(() => new TorrentValidationConfig(), logger);

        await service.ValidateFilesAsync("a", [File("Show.S01E01.mkv", 1_000_000_000)]);

        logger.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task DisabledValidation_IsLoggedAsWarningOnce()
    {
        var logger = new RecordingLogger();
        var config = new TorrentValidationConfig { EnableContentValidation = false };
        var service = new TorrentContentValidationService(() => config, logger);

        await service.ValidateFilesAsync("a", [File("Setup.exe", 3_000_000)]);
        await service.ValidateFilesAsync("b", [File("Setup.exe", 3_000_000)]);

        logger.Warnings.Should().ContainSingle(message => message.Contains("disabled"));
    }

    private sealed class RecordingLogger : ITorrentContentValidationLogger
    {
        public List<string> Warnings { get; } = [];

        public void Debug(string message)
        {
        }

        public void Info(string message)
        {
        }

        public void Warning(string message) => Warnings.Add(message);
    }
}
