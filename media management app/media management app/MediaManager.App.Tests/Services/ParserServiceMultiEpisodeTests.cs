using System.IO;
using media_management_app.Common;
using media_management_app.Services;
using MediaManager.App.Tests.Cleanup;

namespace MediaManager.App.Tests.Services;

// Import Existing / library scan path: a file holding two episodes must keep the range, so the library
// marks both episodes available and Jellyfin sees one "S01E01-E02" item (same rule as the torrent pack path).
public sealed class ParserServiceMultiEpisodeTests
{
    private readonly ParserService _parser = new(new FakeAppLogger());

    [Theory]
    [InlineData("Show.Name.S01E01E02.1080p.WEB-DL.mkv", 1, 1, 2)]
    [InlineData("Show.Name.S01E01-E02.1080p.WEB-DL.mkv", 1, 1, 2)]
    [InlineData("Show Name - S02E05-06 - Two Part Title.mkv", 2, 5, 6)]
    [InlineData("Show.Name.S01E03E04E05.mkv", 1, 3, 5)]
    public void Parse_MultiEpisodeFile_KeepsTheRange(string fileName, int season, int episode, int lastEpisode)
    {
        var parsed = _parser.Parse(fileName, @"D:\Media\Show Name");

        Assert.Equal(MediaKind.TvEpisode, parsed.MediaKind);
        Assert.False(parsed.NeedsReview);
        Assert.Equal("Show Name", parsed.ShowTitle);
        Assert.Equal(season, parsed.SeasonNumber);
        Assert.Equal(episode, parsed.EpisodeNumber);
        Assert.Equal(lastEpisode, parsed.LastEpisodeNumber);
    }

    [Fact]
    public void Parse_MultiEpisodeFile_DoesNotKeepTheSecondNumberAsTitle()
    {
        var parsed = _parser.Parse("Show Name - S02E05-06 - Two Part Title.mkv", @"D:\Media\Show Name");

        Assert.Equal("Two Part Title", parsed.EpisodeTitle);
    }

    [Theory]
    [InlineData("Show.Name.S01E01.1080p.mkv")]
    [InlineData("Show.Name.S01E01.2160p.HDR.mkv")]
    [InlineData("Show Name - S01E09 - 1080p.mkv")]
    [InlineData("Show.Name.S01E01.2019.mkv")]
    public void Parse_SingleEpisodeFile_HasNoRange(string fileName)
    {
        var parsed = _parser.Parse(fileName, @"D:\Media\Show Name");

        Assert.Equal(MediaKind.TvEpisode, parsed.MediaKind);
        Assert.Null(parsed.LastEpisodeNumber);
    }

    [Fact]
    public void Scan_MultiEpisodeFile_CarriesTheRangeIntoTheSourceItem()
    {
        var root = Path.Combine(Path.GetTempPath(), "mm-parser-multi-" + Guid.NewGuid().ToString("N"));
        var showFolder = Path.Combine(root, "Show Name");
        Directory.CreateDirectory(showFolder);
        try
        {
            File.WriteAllText(Path.Combine(showFolder, "Show.Name.S01E01E02.mkv"), string.Empty);
            var scanner = new ScannerService(_parser, new FakeAppLogger());

            var item = Assert.Single(scanner.Scan([root]));

            Assert.Equal(1, item.EpisodeNumber);
            Assert.Equal(2, item.LastEpisodeNumber);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
