using FluentAssertions;
using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.Core.Tests.Validation;

/// <summary>
/// SEC-A12: Windows drops trailing dots and spaces when it opens a file, and bidirectional control characters change
/// how a name is shown. libtorrent on Windows strips both, but a qBittorrent on Linux or a NAS does not, so the
/// validator reads the extension of the name Windows will actually use.
/// </summary>
public class FileNameNormalizationTests
{
    private const long MB = 1024 * 1024;

    private static TorrentContentValidationService Create() => new(() => new TorrentValidationConfig());

    private static TorrentContentFile File(string name, long size) => new() { Name = name, Size = size };

    [Theory]
    [InlineData("Setup.exe ")]
    [InlineData("Setup.exe.")]
    [InlineData("Setup.exe. . ")]
    [InlineData("Setup.ex⁦e")]
    [InlineData("Setup.exe‏")]
    [InlineData("Play.lnk؜")]
    public async Task ExecutableHiddenByTrailingOrInvisibleCharacters_IsRejected(string name)
    {
        var result = await Create().ValidateFilesAsync(
            "abc",
            [File("Show.NN.S01E01.1080p.mkv", 1024 * MB), File(name, 3 * MB)],
            listingName: "Show.NN.S01E01.1080p");

        result.IsValid.Should().BeFalse();
        result.SuspiciousFiles.Should().Contain(file => file.SuspicionLevel == SuspicionLevel.Critical);
    }

    [Fact]
    public async Task RightToLeftOverride_IsRejectedEvenWithAVideoExtension()
    {
        var result = await Create().ValidateFilesAsync(
            "abc",
            [File("Show.NN.S01E01.‮exe.mkv", 1024 * MB)],
            listingName: "Show.NN.S01E01");

        result.IsValid.Should().BeFalse();
        result.SuspiciousFiles.Should().Contain(file => file.Reason.Contains("direction", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task VideoWithTrailingMarkFromRightToLeftTitle_IsValid()
    {
        var result = await Create().ValidateFilesAsync(
            "abc",
            [File("Show.NN.S01E01.1080p.mkv‏", 1024 * MB), File("Show.NN.S01E01.ar.srt؜", 40_000)],
            listingName: "Show.NN.S01E01.1080p");

        result.IsValid.Should().BeTrue(string.Join("; ", result.SuspiciousFiles.Select(file => file.Reason)));
    }
}
