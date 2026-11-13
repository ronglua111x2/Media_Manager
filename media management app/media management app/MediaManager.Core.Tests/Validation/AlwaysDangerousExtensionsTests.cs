using FluentAssertions;
using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.Core.Tests.Validation;

/// <summary>
/// Security (cross-review 2, user-approved): Windows shortcuts and other launchers (.lnk, .hta, .wsf...) are always
/// dangerous, also when settings.json saved an older DangerousExtensions list without them. ".url" stays harmless.
/// </summary>
public class AlwaysDangerousExtensionsTests
{
    // The list a real settings.json saved before .lnk was dangerous (41 entries, no .lnk).
    private static readonly List<string> SavedOldList =
    [
        ".exe", ".scr", ".bat", ".cmd", ".com", ".pif", ".vbs", ".js", ".jar", ".ps1", ".psm1", ".psd1",
        ".dll", ".sys", ".drv", ".ocx", ".cpl", ".msi", ".app", ".dmg", ".sh", ".bash", ".ksh", ".csh", ".run"
    ];

    private static TorrentContentValidationService Create(TorrentValidationConfig config) => new(() => config);

    private static TorrentContentFile File(string name, long size) => new() { Name = name, Size = size };

    [Fact]
    public async Task ShortcutDisguisedAsVideo_IsRejected_WithDefaultConfig()
    {
        var result = await Create(new TorrentValidationConfig()).ValidateFilesAsync("abc", [File("Movie.mkv.lnk", 2_000)]);

        result.IsValid.Should().BeFalse();
        result.Recommendation.Should().Be(TorrentHandleRecommendation.Delete);
    }

    [Fact]
    public async Task ShortcutDisguisedAsVideo_IsRejected_WithASavedListWithoutLnk()
    {
        var config = new TorrentValidationConfig { DangerousExtensions = SavedOldList };

        var result = await Create(config).ValidateFilesAsync("abc", [File("Movie.mkv.lnk", 2_000)]);

        result.IsValid.Should().BeFalse();
        result.SuspiciousFiles.Should().Contain(file => file.Extension == ".lnk" && file.SuspicionLevel == SuspicionLevel.Critical);
    }

    [Theory]
    [InlineData("Play.lnk")]
    [InlineData("Setup.hta")]
    [InlineData("Run.wsf")]
    [InlineData("Codec.reg")]
    [InlineData("Help.chm")]
    [InlineData("Player.msix")]
    [InlineData("Player.appinstaller")]
    public async Task LauncherNextToAVideo_IsRejected(string launcher)
    {
        var config = new TorrentValidationConfig { DangerousExtensions = SavedOldList };

        var result = await Create(config).ValidateFilesAsync("abc", [File("Movie.2014.1080p.mkv", 2_000_000_000), File(launcher, 2_000)]);

        result.IsValid.Should().BeFalse();
    }

    // SEC-A04: disk images mount on double-click and drop Mark-of-the-Web; .library-ms / .searchConnector-ms /
    // .settingcontent-ms / .website leak NTLM or run commands; ClickOnce and Excel add-ins install or run code.
    [Theory]
    [InlineData("Codec.Pack.iso")]
    [InlineData("Player.img")]
    [InlineData("Player.vhd")]
    [InlineData("Player.vhdx")]
    [InlineData("Subs.library-ms")]
    [InlineData("Subs.searchConnector-ms")]
    [InlineData("Fix.settingcontent-ms")]
    [InlineData("Watch.website")]
    [InlineData("Player.application")]
    [InlineData("Player.appref-ms")]
    [InlineData("Subtitles.xll")]
    [InlineData("x.wsc")]
    [InlineData("x.sct")]
    [InlineData("Fix.diagcab")]
    [InlineData("Patch.msp")]
    public async Task PayloadContainerOrLauncherNextToAVideo_IsRejected(string extra)
    {
        var config = new TorrentValidationConfig { DangerousExtensions = SavedOldList };

        var result = await Create(config).ValidateFilesAsync("abc", [File("Movie.2014.1080p.mkv", 2_000_000_000), File(extra, 40_000_000)]);

        result.IsValid.Should().BeFalse();
        result.SuspiciousFiles.Should().Contain(file => file.FileName == extra && file.SuspicionLevel == SuspicionLevel.Critical);
    }

    [Fact]
    public async Task VideoWithAUrlFile_IsValid()
    {
        var result = await Create(new TorrentValidationConfig()).ValidateFilesAsync(
            "abc",
            [File("Movie.2014.1080p.mkv", 2_000_000_000), File("Visit.url", 120)]);

        result.IsValid.Should().BeTrue();
    }
}
