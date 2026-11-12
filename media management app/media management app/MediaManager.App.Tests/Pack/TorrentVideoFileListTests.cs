using media_management_app.Models;

namespace MediaManager.App.Tests.Pack;

public class TorrentVideoFileListTests
{
    // PARSE-13 (extension part): torrent metadata and qBittorrent file lists use one extension list.
    [Fact]
    public void MetadataProbe_CountsSameVideoExtensionsAsQbittorrentFiles()
    {
        var paths = new[] { "a.mkv", "b.wmv", "c.ts", "d.m2ts", "e.webm", "f.srt", "g.nfo" };
        var probe = new TorrentMetadataProbeResult
        {
            Files = paths.Select(path => new TorrentMetadataFile { Path = path, Length = 1 }).ToList()
        };

        Assert.Equal(5, probe.VideoFileCount);
        Assert.Equal(
            paths.Where(path => new TorrentContentFile { Name = path }.IsVideoFile),
            probe.VideoFiles.Select(file => file.Path));
    }
}
