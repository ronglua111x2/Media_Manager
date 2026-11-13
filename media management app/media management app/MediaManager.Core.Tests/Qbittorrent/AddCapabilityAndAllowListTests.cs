using FluentAssertions;
using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.Core.Tests.Qbittorrent;

/// <summary>
/// S0 (SEC-A03): which qBittorrent can check a torrent before it downloads, and which files are worth downloading.
/// </summary>
public class AddCapabilityAndAllowListTests
{
    [Theory]
    [InlineData("2.8.5", false, false)]   // 4.4.5
    [InlineData("2.8.18", true, false)]   // 4.5.0: torrents/add stopCondition
    [InlineData("2.11.4", true, false)]   // 5.1.x
    [InlineData("2.15.1", true, true)]    // 5.2.x: torrents/fetchMetadata
    [InlineData("2.17.0", true, true)]
    [InlineData("", false, false)]
    [InlineData("garbage", false, false)]
    public void WebApiVersion_SelectsAddStrategy(string version, bool stopCondition, bool fetchMetadata)
    {
        var parsed = QbittorrentWebApiCompatibility.ParseWebApiVersion(version);

        QbittorrentWebApiCompatibility.SupportsStopCondition(parsed).Should().Be(stopCondition);
        QbittorrentWebApiCompatibility.SupportsFetchMetadata(parsed).Should().Be(fetchMetadata);
    }

    [Theory]
    [InlineData("Show.NN.S01E01.1080p.mkv", true)]
    [InlineData("Show/Show.NN.S01E01.mp4", true)]
    [InlineData("Show.NN.S01E01.en.srt", true)]
    [InlineData("Subs/Show.NN.S01E01.ass", true)]
    [InlineData("Show.NN.S01E01.mka", true)]
    [InlineData("Visit Our Site.url", false)]
    [InlineData("Show.NN.S01E01.nfo", false)]
    [InlineData("cover.jpg", false)]
    [InlineData("Codec.zip", false)]
    [InlineData("README.txt", false)]
    [InlineData("Show.NN.S01E01.mkv.", true)]
    public void DownloadAllowList_KeepsVideoAndSubtitlesOnly(string path, bool wanted)
    {
        TorrentDownloadAllowList.IsWanted(path, new TorrentValidationConfig()).Should().Be(wanted);
    }
}
