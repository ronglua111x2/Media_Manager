using FluentAssertions;
using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.Core.Tests.Validation;

public class TorrentContentValidationServiceTests
{
    [Fact]
    public async Task ValidateFilesAsync_Disabled_IsValid()
    {
        var sut = Create(new TorrentValidationConfig { EnableContentValidation = false });

        var result = await sut.ValidateFilesAsync("abc", [File("virus.exe", 100)]);

        result.IsValid.Should().BeTrue();
        result.Recommendation.Should().Be(TorrentHandleRecommendation.Safe);
    }

    [Fact]
    public async Task ValidateFilesAsync_EmptyList_RequiresReview()
    {
        var sut = Create();

        var result = await sut.ValidateFilesAsync("abc", []);

        result.IsValid.Should().BeTrue();
        result.Recommendation.Should().Be(TorrentHandleRecommendation.ReviewRequired);
        result.ValidationErrors.Should().Contain(e => e.Contains("empty", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateFilesAsync_Exe_FailsCritical()
    {
        var sut = Create();
        var files = LoadGoldenFiles("virus-exe");

        var result = await sut.ValidateFilesAsync("abc", files);

        result.IsValid.Should().BeFalse();
        result.Recommendation.Should().Be(TorrentHandleRecommendation.Delete);
        result.SuspiciousFiles.Should().Contain(f => f.Extension == ".exe" && f.SuspicionLevel == SuspicionLevel.Critical);
    }

    [Fact]
    public async Task ValidateFilesAsync_DoubleExtensionDisguise_FailsObfuscation()
    {
        var sut = Create();

        var result = await sut.ValidateFilesAsync("abc", [File("Show.S01E01.mkv.exe", 400_000)]);

        result.IsValid.Should().BeFalse();
        result.SuspiciousFiles.Should().Contain(f =>
            f.Reason.Contains("obfuscation", StringComparison.OrdinalIgnoreCase) ||
            f.Extension == ".exe");
    }

    [Fact]
    public async Task ValidateFilesAsync_SafeMkv_Passes()
    {
        var sut = Create();

        var result = await sut.ValidateFilesAsync("abc", [File("Show.S01E01.1080p.mkv", 1_400_000_000)]);

        result.IsValid.Should().BeTrue();
        result.SuspiciousFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateFilesAsync_NfoExtra_IsIgnored()
    {
        var sut = Create();

        var result = await sut.ValidateFilesAsync(
            "abc",
            [File("Show.S01E01.1080p.mkv", 1_400_000_000), File("Show.S01E01.nfo", 400)],
            listingName: "Show.S01E01.1080p");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateFilesAsync_PayloadMismatchZip_Fails()
    {
        var sut = Create();

        var result = await sut.ValidateFilesAsync(
            "abc",
            [File("Show.S01E01.1080p.zip", 1_400_000_000)],
            listingName: "Show.S01E01.1080p");

        result.IsValid.Should().BeFalse();
        result.SuspiciousFiles.Should().Contain(f =>
            f.Reason.Contains("not an allowed media format", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateFilesAsync_CustomDangerousExtension_Fails()
    {
        var sut = Create(new TorrentValidationConfig
        {
            CustomDangerousExtensions = [".xyz"]
        });

        var result = await sut.ValidateFilesAsync("abc", [File("payload.xyz", 50_000)]);

        result.IsValid.Should().BeFalse();
        result.SuspiciousFiles.Should().Contain(f => f.Extension == ".xyz");
    }

    [Fact]
    public async Task ValidateFilesAsync_PackIgnoresSmallSampleWhenLargerVideosExist()
    {
        var sut = Create();
        var files = new[]
        {
            File("Season 01/E01.mkv", 900_000_000),
            File("Season 01/E02.mkv", 910_000_000),
            File("Season 01/E03.mkv", 905_000_000),
            File("sample/sample.mkv", 12_000_000),
            File("Season 01/E01.nfo", 400)
        };

        var result = await sut.ValidateFilesAsync(
            "abc",
            files,
            listingName: "Show Season 01",
            isPack: true);

        result.IsValid.Should().BeTrue();
        result.SuspiciousFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateFilesAsync_Scr_Fails()
    {
        var sut = Create();

        var result = await sut.ValidateFilesAsync("abc", [File("install.scr", 80_000)]);

        result.IsValid.Should().BeFalse();
        result.SuspiciousFiles.Should().Contain(f => f.Extension == ".scr");
    }

    private static TorrentContentValidationService Create(TorrentValidationConfig? config = null) =>
        new(() => config ?? new TorrentValidationConfig());

    private static IReadOnlyList<TorrentContentFile> LoadGoldenFiles(string id)
    {
        var files = MediaManager.Core.Tests.Fixtures.GoldenFixtureFile.Root("validation-file-lists.json")
            .GetProperty("cases")
            .EnumerateArray()
            .First(item => item.GetProperty("id").GetString() == id)
            .GetProperty("files");

        return files.EnumerateArray()
            .Select(file => File(file.GetProperty("name").GetString()!, file.GetProperty("size").GetInt64()))
            .ToList();
    }

    private static TorrentContentFile File(string name, long size) =>
        new() { Name = name, Size = size };
}
