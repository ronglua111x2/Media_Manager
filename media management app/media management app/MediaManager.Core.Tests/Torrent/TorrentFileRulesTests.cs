using FluentAssertions;
using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.Core.Tests.Torrent;

public class TorrentFileRulesTests
{
    [Theory]
    [InlineData("Show.S01E05.1080p/Sample/show.s01e05.1080p-sample.mkv", true)]
    [InlineData("Show.S01.1080p/Sample/Show.S01E01.sample.mkv", true)]
    [InlineData("Show.S01.1080p/Samples/Show.S01E01.mkv", true)]
    [InlineData("Show.S01E01.1080p/sample-show.s01e01.1080p.mkv", true)]
    [InlineData("Show.S01E01.1080p/Proof/show.s01e01.mkv", true)]
    [InlineData("Show.S01E01.1080p/show.s01e01.1080p-proof.mkv", true)]
    [InlineData("Show.S01E01.1080p.mkv", false)]
    [InlineData("Proof.S01E01.1080p.mkv", false)]
    [InlineData("Sample.Show.S01E01.1080p.mkv", false)]
    [InlineData("Show/Season 1/Show.S01E04.Free.Samples.Day.mkv", false)]
    [InlineData("Show/Season 1/Show.S01E04.Burden.of.Proof.mkv", false)]
    [InlineData("Samplers/Show.S01E01.mkv", false)]
    public void IsSampleOrProof_UsesFolderOrSceneSuffix(string relativePath, bool expected)
    {
        TorrentFileRules.IsSampleOrProof(relativePath).Should().Be(expected);
    }

    [Theory]
    [InlineData("a.mkv", true)]
    [InlineData("a.MP4", true)]
    [InlineData("a.wmv", true)]
    [InlineData("a.ts", true)]
    [InlineData("BDMV/STREAM/00001.m2ts", true)]
    [InlineData("a.webm", true)]
    [InlineData("a.srt", false)]
    [InlineData("a.nfo", false)]
    [InlineData("a", false)]
    public void IsVideoFile_UsesSharedExtensionList(string path, bool expected)
    {
        TorrentFileRules.IsVideoFile(path).Should().Be(expected);
        new TorrentContentFile { Name = path }.IsVideoFile.Should().Be(expected);
    }

    [Theory]
    [InlineData("Show.S01E03v2.1080p", 102)]
    [InlineData("[Group] Show - 05v2 [1080p]", 102)]
    [InlineData("Show - 05 v3", 103)]
    [InlineData("Show.S01E03.PROPER.1080p", 50)]
    [InlineData("Show.S01E03.REPACK.1080p", 50)]
    [InlineData("Show.S01E03.1080p", 0)]
    [InlineData("Show.S01E03.1080p.x264", 0)]
    public void GetRevisionScore_ReadsGluedAndSeparateRevisions(string stem, int expected)
    {
        TorrentFileRules.GetRevisionScore(stem).Should().Be(expected);
    }

    [Fact]
    public void SelectEpisodeFile_SampleListedFirst_PicksMainFile()
    {
        var files = new[]
        {
            File("Show.S01E05.1080p/Sample/show.s01e05.1080p-sample.mkv", 40_000_000),
            File("Show.S01E05.1080p/Show.S01E05.1080p.mkv", 1_500_000_000),
        };

        TorrentFileRules.SelectEpisodeFile(files, 1, 5)!.Name.Should().Be("Show.S01E05.1080p/Show.S01E05.1080p.mkv");
    }

    [Fact]
    public void SelectEpisodeFile_OnlySample_ReturnsNull()
    {
        var files = new[]
        {
            File("Show.S01E05.1080p/Sample/show.s01e05.1080p-sample.mkv", 40_000_000),
            File("Show.S01E05.1080p/Show.S01E06.1080p.mkv", 1_500_000_000),
        };

        TorrentFileRules.SelectEpisodeFile(files, 1, 5).Should().BeNull();
    }

    [Fact]
    public void SelectEpisodeFile_PrefersRevisionThenQualityThenSize()
    {
        var files = new[]
        {
            File("Pack/Show.S01E03.1080p.mkv", 2_000_000_000),
            File("Pack/Show.S01E03v2.720p.mkv", 500_000_000),
            File("Pack/Show.S01E04.720p.mkv", 900_000_000),
            File("Pack/Show.S01E04.1080p.mkv", 800_000_000),
            File("Pack/a/Show.S01E05.1080p.mkv", 800_000_000),
            File("Pack/b/Show.S01E05.1080p.mkv", 900_000_000),
        };

        TorrentFileRules.SelectEpisodeFile(files, 1, 3)!.Name.Should().Be("Pack/Show.S01E03v2.720p.mkv");
        TorrentFileRules.SelectEpisodeFile(files, 1, 4)!.Name.Should().Be("Pack/Show.S01E04.1080p.mkv");
        TorrentFileRules.SelectEpisodeFile(files, 1, 5)!.Name.Should().Be("Pack/b/Show.S01E05.1080p.mkv");
    }

    [Fact]
    public void SelectEpisodeFile_FileNameWinsOverFolderName()
    {
        var files = new[]
        {
            File("Show.S01E01-E02.1080p/Show.S01E01.1080p.mkv", 1_000_000_000),
            File("Show.S01E01-E02.1080p/Show.S01E02.1080p.mkv", 1_000_000_000),
        };

        TorrentFileRules.SelectEpisodeFile(files, 1, 2)!.Name.Should().Be("Show.S01E01-E02.1080p/Show.S01E02.1080p.mkv");
    }

    // Audit L1-M1: the folder's S01E01-E03 is a range for the folder, not for each file in it. The largest
    // file used to win, so "- 02" could be linked as E01.
    [Theory]
    [InlineData(1, "[Group] Show S01E01-E03 [1080p]/[Group] Show - 01 [1080p].mkv")]
    [InlineData(2, "[Group] Show S01E01-E03 [1080p]/[Group] Show - 02 [1080p].mkv")]
    [InlineData(3, "[Group] Show S01E01-E03 [1080p]/[Group] Show - 03 [1080p].mkv")]
    public void SelectEpisodeFile_RangeOnlyInFolderName_PicksByTheFilesOwnNumber(int episode, string expected)
    {
        var files = new[]
        {
            File("[Group] Show S01E01-E03 [1080p]/[Group] Show - 01 [1080p].mkv", 900_000_000),
            File("[Group] Show S01E01-E03 [1080p]/[Group] Show - 02 [1080p].mkv", 1_200_000_000),
            File("[Group] Show S01E01-E03 [1080p]/[Group] Show - 03 [1080p].mkv", 1_000_000_000),
        };

        TorrentFileRules.SelectEpisodeFile(files, 1, episode)!.Name.Should().Be(expected);
    }

    [Fact]
    public void SelectEpisodeFile_RangeOnlyInFolderName_NoFileNumber_ReturnsNull()
    {
        var files = new[]
        {
            File("Show.S01E01-E02.1080p/abc.mkv", 900_000_000),
            File("Show.S01E01-E02.1080p/def.mkv", 1_200_000_000),
        };

        TorrentFileRules.SelectEpisodeFile(files, 1, 1).Should().BeNull();
    }

    [Fact]
    public void SelectEpisodeFile_EpisodeOnlyInFolderName_StillMatches()
    {
        var files = new[]
        {
            File("Show.S01E05.1080p.WEB/abc123.mkv", 1_000_000_000),
            File("Show.S01E05.1080p.WEB/abc123.mp4", 900_000_000),
        };

        TorrentFileRules.SelectEpisodeFile(files, 1, 5)!.Name.Should().Be("Show.S01E05.1080p.WEB/abc123.mkv");
    }

    [Fact]
    public void SelectEpisodeFile_GluedRevision_IsParsed()
    {
        var files = new[]
        {
            File("Pack/Show.S01E02v2.1080p.mkv", 1_000_000_000),
            File("Pack/Show.S01E03v2.1080p.mkv", 1_000_000_000),
        };

        TorrentFileRules.SelectEpisodeFile(files, 1, 3)!.Name.Should().Be("Pack/Show.S01E03v2.1080p.mkv");
    }

    [Fact]
    public void SelectEpisodeFile_MultiEpisodeFile_MatchesEveryEpisodeOfItsRange()
    {
        var files = new[]
        {
            File("Pack/Show.S01E01E02.1080p.mkv", 2_000_000_000),
            File("Pack/Show.S01E03.1080p.mkv", 1_000_000_000),
        };

        TorrentFileRules.SelectEpisodeFile(files, 1, 1)!.Name.Should().Be("Pack/Show.S01E01E02.1080p.mkv");
        TorrentFileRules.SelectEpisodeFile(files, 1, 2)!.Name.Should().Be("Pack/Show.S01E01E02.1080p.mkv");
        TorrentFileRules.SelectEpisodeFile(files, 1, 3)!.Name.Should().Be("Pack/Show.S01E03.1080p.mkv");
    }

    private static TorrentContentFile File(string name, long size) =>
        new() { Name = name, Size = size, Progress = 1 };
}
