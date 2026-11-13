using FluentAssertions;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.Core.Tests.Validation;

/// <summary>
/// SEC-A05: the payload check looked only at the file whose name best matched the listing, so a tiny decoy video
/// named like the release hid a large archive or other non-media payload. Now non-media bytes are limited and, when the
/// caller knows the target, the largest video must reach a size floor.
/// </summary>
public class DecoyPayloadTests
{
    private const long KB = 1024;
    private const long MB = 1024 * KB;
    private const long GB = 1024 * MB;

    private static TorrentContentValidationService Create() => new(() => new TorrentValidationConfig());

    private static TorrentContentFile File(string name, long size) => new() { Name = name, Size = size };

    [Fact]
    public async Task TinyDecoyVideoWithLargeArchive_IsRejected()
    {
        var result = await Create().ValidateFilesAsync(
            "abc",
            [File("Movie.NN.2024.1080p.mkv", 20 * KB), File("Movie.NN.2024.1080p.part1.rar", 2 * GB)],
            listingName: "Movie.NN.2024.1080p");

        result.IsValid.Should().BeFalse();
        result.SuspiciousFiles.Should().Contain(file => file.FileName == "Movie.NN.2024.1080p.part1.rar");
    }

    [Fact]
    public async Task PackWithLargeArchiveBeyondTopThree_IsRejected()
    {
        var files = Enumerable.Range(1, 10)
            .Select(episode => File($"Show.NN.S01E{episode:00}.1080p.mkv", 1 * GB))
            .Append(File("Bonus.Player.rar", 300 * MB))
            .ToList();

        var result = await Create().ValidateFilesAsync("abc", files, listingName: "Show.NN.S01.1080p", isPack: true);

        result.IsValid.Should().BeFalse();
        result.SuspiciousFiles.Should().Contain(file => file.FileName == "Bonus.Player.rar");
    }

    [Fact]
    public async Task SmallTorrentMostlyNonMedia_IsRejected()
    {
        var result = await Create().ValidateFilesAsync(
            "abc",
            [File("Show.NN.S01E01.mkv", 30 * MB), File("Codec.zip", 12 * MB)],
            listingName: "Show.NN.S01E01");

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task AnimePackWithFontsScansAndLosslessAudio_IsValid()
    {
        var files = Enumerable.Range(1, 12)
            .Select(episode => File($"[Grp] Show NN - {episode:00} [1080p].mkv", 300 * MB))
            .Concat(Enumerable.Range(1, 40).Select(index => File($"Fonts/font{index}.ttf", 400 * KB)))
            .Concat(Enumerable.Range(1, 20).Select(index => File($"Scans/{index:00}.png", 5 * MB)))
            .Concat(Enumerable.Range(1, 10).Select(index => File($"CDs/OST/{index:00}.flac", 40 * MB)))
            .Append(File("CDs/OST/OST.cue", 2 * KB))
            .Append(File("[Grp] Show NN - 01 [1080p].mka", 120 * MB))
            .Append(File("Fonts.7z", 4 * MB))
            .ToList();

        var result = await Create().ValidateFilesAsync("abc", files, listingName: "[Grp] Show NN (BD 1080p)", isPack: true);

        result.IsValid.Should().BeTrue(string.Join("; ", result.SuspiciousFiles.Select(file => $"{file.FileName}: {file.Reason}")));
    }

    [Theory]
    [InlineData(MediaKind.TvEpisode, "Show.NN.S01E01.1080p.mkv", 30 * KB)]
    [InlineData(MediaKind.TvSeasonPack, "Show.NN.S01E01.1080p.mkv", 50 * KB)]
    [InlineData(MediaKind.Movie, "Movie.NN.2024.1080p.wmv", 6 * MB)]
    public async Task LargestVideoBelowFloor_IsRejected(MediaKind kind, string video, long size)
    {
        var result = await Create().ValidateFilesAsync(
            "abc",
            [File(video, size), File("Readme.txt", 1 * KB)],
            listingName: Path.GetFileNameWithoutExtension(video),
            isPack: kind == MediaKind.TvSeasonPack,
            targetKind: kind);

        result.IsValid.Should().BeFalse();
        result.SuspiciousFiles.Should().Contain(file => file.FileName == video);
    }

    [Theory]
    [InlineData(MediaKind.TvEpisode, 45 * MB)]
    [InlineData(MediaKind.Movie, 400 * MB)]
    public async Task SmallButRealisticEncode_IsValid(MediaKind kind, long size)
    {
        var result = await Create().ValidateFilesAsync(
            "abc",
            [File("Title.NN.480p.x265.mkv", size)],
            listingName: "Title.NN.480p.x265",
            targetKind: kind);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task UnknownTarget_HasNoSizeFloor()
    {
        var result = await Create().ValidateFilesAsync("abc", [File("Show.NN.S01E01.mkv", 2 * KB)]);

        result.IsValid.Should().BeTrue();
    }
}
