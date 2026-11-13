using System.Security.Cryptography;
using System.Text;
using media_management_app.Services;

namespace MediaManager.App.Tests.Services;

/// <summary>S0: reading a torrent's file list before it is added (own .torrent parse, qBittorrent fetchMetadata).</summary>
public class PreAddMetadataTests
{
    [Fact]
    public void TorrentBytes_InfohashIsSha1OfInfoAndPaddingFilesAreSkipped()
    {
        const string info =
            "d5:filesl" +
            "d6:lengthi1000e4:pathl8:Ep01.mkveed4:attr1:p6:lengthi24e4:pathl4:.pad2:24eed6:lengthi20e4:pathl8:Site.urlee" +
            "e4:name4:Show12:piece lengthi16384e6:pieces0:e";
        var bytes = Encoding.ASCII.GetBytes("d8:announce3:x:y4:info" + info + "e");

        var result = TorrentMetadataReader.Read(bytes);

        Assert.True(result.IsAvailable);
        Assert.Equal(new[] { "Ep01.mkv", "Site.url" }, result.Files.Select(file => file.Path));
        Assert.Equal(Convert.ToHexString(SHA1.HashData(Encoding.ASCII.GetBytes(info))).ToLowerInvariant(), result.InfoHashV1);
    }

    [Fact]
    public void TorrentBytes_V2OnlyIsNotReadBeforeAdd()
    {
        var bytes = Encoding.ASCII.GetBytes("d4:infod9:file treede4:name4:Show12:meta versioni2eee");

        Assert.False(TorrentMetadataReader.Read(bytes).IsAvailable);
    }

    [Fact]
    public void FetchedMetadata_FinishedAnswer_IsParsedInFileOrder()
    {
        const string magnet = "magnet:?xt=urn:btih:dddd0000000000000000000000000000000000e5";
        const string json = """
            {"hash":"dddd0000000000000000000000000000000000e5","infohash_v1":"dddd0000000000000000000000000000000000e5",
             "info":{"name":"Show.NN.S01E01","length":1020,"files":[{"path":"Show.NN.S01E01/ep.mkv","length":1000},{"path":"Show.NN.S01E01/Site.url","length":20}]}}
            """;

        var inspection = QbittorrentFetchedMetadata.Parse(magnet, json);

        Assert.NotNull(inspection);
        Assert.True(inspection!.MetadataPrefetched);
        Assert.Equal("dddd0000000000000000000000000000000000e5", inspection.InfoHash);
        Assert.Equal(new[] { "Show.NN.S01E01/ep.mkv", "Show.NN.S01E01/Site.url" }, inspection.Files.Select(file => file.Name));
        Assert.Equal(magnet, inspection.AddUrl);
    }

    [Fact]
    public void FetchedMetadata_StillFetchingAnswer_IsNull()
    {
        Assert.Null(QbittorrentFetchedMetadata.Parse("magnet:?xt=urn:btih:x", """{"hash":"dddd0000000000000000000000000000000000e5"}"""));
    }
}
