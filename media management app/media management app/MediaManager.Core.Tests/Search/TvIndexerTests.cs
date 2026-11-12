using FluentAssertions;
using media_management_app.Common;
using media_management_app.Search;
using media_management_app.Search.Indexers;
using MediaManager.Core.Tests.Fixtures;

namespace MediaManager.Core.Tests.Search;

/// <summary>EZTV (IMDb API) and TorrentDownloads (search RSS) against real responses captured 12/11/2026.</summary>
public class TvIndexerTests
{
    private const string RickAndMortyPage1 = "rick-and-morty.json";
    private const string RickAndMortyPage2 = "rick-and-morty-page2.json";

    private static string Fixture(string indexer, string file) =>
        File.ReadAllText(GoldenFixtureFile.PathTo(Path.Combine("Indexers", indexer, file)));

    private static IndexerQuery TvQuery(string? imdbId, int? season = null, int? episode = null) =>
        new("Rick and Morty", episode is null && season is not null ? MediaKind.TvSeasonPack : MediaKind.TvEpisode,
            ImdbId: imdbId, Season: season, Episode: episode);

    // Serves eztv page 1 / page 2 fixtures by the "page" query parameter.
    private static FakeIndexerHttp EztvPages() => new(uri =>
        uri.Query.Contains("page=2", StringComparison.Ordinal)
            ? Fixture("eztv", RickAndMortyPage2)
            : Fixture("eztv", RickAndMortyPage1));

    [Fact]
    public void Eztv_ParsesRowsWithHashMagnetSeedersSizeAndDate()
    {
        var page = EztvIndexer.ParsePage(Fixture("eztv", RickAndMortyPage1));

        page.TotalCount.Should().Be(582);
        page.Rows.Should().HaveCount(100);
        page.Rows.Should().OnlyContain(row => IndexerResultNormalizer.IsValidInfoHash(row.InfoHash));
        page.Rows.Should().OnlyContain(row => row.MagnetUri!.StartsWith("magnet:?xt=urn:btih:"));

        var first = page.Rows[0];
        first.Title.Should().Be("Rick and Morty S09E04 A Ricker Runs Through It 1080p BluRay Remux DTS-HD MA5 1 H 264-NTb");
        first.InfoHash.Should().Be("65df4996a94d5df18d2ccda69865ef7cf7a1ad4c");
        first.Seeders.Should().Be(87);
        first.Leechers.Should().Be(19);
        first.SizeBytes.Should().Be(4_042_298_149);
        first.PublishedUtc.Should().Be(new DateTimeOffset(2026, 11, 4, 3, 6, 59, TimeSpan.Zero));
        first.IndexerId.Should().Be("eztv");
    }

    [Fact]
    public void Eztv_EpisodeFilter_KeepsOnlyThatEpisode()
    {
        var rows = EztvIndexer.Parse(Fixture("eztv", RickAndMortyPage1), season: 9, episode: 4);

        rows.Should().HaveCount(7);
        rows.Should().OnlyContain(row => row.Title.Contains("S09E04", StringComparison.Ordinal));
    }

    [Fact]
    public void Eztv_SeasonFilter_KeepsPacksAndEpisodesOfThatSeason()
    {
        var rows = EztvIndexer.Parse(Fixture("eztv", RickAndMortyPage1), season: 8);

        rows.Should().HaveCount(28);
        rows.Should().OnlyContain(row => row.Title.Contains("S08", StringComparison.Ordinal));
        rows.Select(row => row.Title).Should().Contain(new[] { "Rick and Morty S08 1080p x265-ELiTE", "Rick and Morty S08 720p x264-FENiX" });
    }

    [Fact]
    public void Eztv_SeasonFilter_SeasonNotOnPage_ReturnsNoRows()
    {
        var page = EztvIndexer.ParsePage(Fixture("eztv", RickAndMortyPage1), season: 3);

        page.Rows.Should().BeEmpty();
        page.TotalCount.Should().Be(582);
    }

    [Fact]
    public void Eztv_Empty_ReturnsNoRows()
    {
        var page = EztvIndexer.ParsePage(Fixture("eztv", "empty.json"));

        page.Rows.Should().BeEmpty();
        page.TotalCount.Should().Be(0);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"error\":\"bad request\"}")]
    [InlineData("{\"torrents_count\":3,\"torrents\":\"nope\"}")]
    public void Eztv_UnexpectedShape_Throws(string json)
    {
        var act = () => EztvIndexer.Parse(json);

        act.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("tt2861424", "2861424")]
    [InlineData("2861424", "2861424")]
    [InlineData("tt0944947", "0944947")]
    [InlineData(" TT2861424 ", "2861424")]
    [InlineData("tt", null)]
    [InlineData("abc", null)]
    [InlineData(null, null)]
    public void Eztv_NormalizeImdbId(string? input, string? expected)
    {
        EztvIndexer.NormalizeImdbId(input).Should().Be(expected);
    }

    [Fact]
    public async Task Eztv_SearchWithoutImdbId_ReturnsEmptyWithoutCallingHttp()
    {
        var http = new FakeIndexerHttp(responder: null);

        var rows = await new EztvIndexer().SearchAsync(TvQuery(imdbId: null, season: 9, episode: 4), http, CancellationToken.None);

        rows.Should().BeEmpty();
        http.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Eztv_UnfilteredSearch_FetchesOnePageWithDigitsOnlyId()
    {
        var http = EztvPages();

        var rows = await new EztvIndexer().SearchAsync(TvQuery("tt2861424"), http, CancellationToken.None);

        rows.Should().HaveCount(100);
        http.Requests.Should().ContainSingle()
            .Which.AbsoluteUri.Should().Be("https://eztvx.to/api/get-torrents?imdb_id=2861424&limit=100&page=1");
    }

    [Fact]
    public async Task Eztv_SeasonFoundOnFirstPage_DoesNotFetchSecondPage()
    {
        var http = EztvPages();

        var rows = await new EztvIndexer().SearchAsync(TvQuery("tt2861424", season: 9, episode: 4), http, CancellationToken.None);

        rows.Should().HaveCount(7);
        http.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task Eztv_SeasonMissingOnFirstPage_FetchesSecondPage()
    {
        var http = EztvPages();

        var rows = await new EztvIndexer().SearchAsync(TvQuery("tt2861424", season: 7), http, CancellationToken.None);

        rows.Should().HaveCount(41);
        rows.Should().OnlyContain(row => row.Title.Contains("S07", StringComparison.Ordinal));
        http.Requests.Select(uri => uri.Query).Should().Equal(
            "?imdb_id=2861424&limit=100&page=1",
            "?imdb_id=2861424&limit=100&page=2");
    }

    [Fact]
    public async Task Eztv_SeasonNotFound_StopsAfterMaxPages()
    {
        var http = EztvPages();

        var rows = await new EztvIndexer().SearchAsync(TvQuery("tt2861424", season: 3), http, CancellationToken.None);

        rows.Should().BeEmpty();
        http.Requests.Should().HaveCount(EztvIndexer.MaxPages);
    }

    [Fact]
    public async Task Eztv_NoResultsForId_DoesNotPage()
    {
        var http = new FakeIndexerHttp(_ => Fixture("eztv", "empty.json"));

        var rows = await new EztvIndexer().SearchAsync(TvQuery("tt9999999", season: 1, episode: 1), http, CancellationToken.None);

        rows.Should().BeEmpty();
        http.Requests.Should().HaveCount(1);
    }

    [Fact]
    public void Eztv_DeclaresImdbOnlyTvCapabilities()
    {
        var capabilities = new EztvIndexer().Capabilities;

        capabilities.SupportsFreeText.Should().BeFalse();
        capabilities.SupportsImdbLookup.Should().BeTrue();
        capabilities.Kinds.Should().Be(IndexerMediaKinds.TvEpisode | IndexerMediaKinds.TvSeasonPack);
    }

    [Fact]
    public void TorrentDownloads_ParsesRssItemsWithHashSeedersAndSize()
    {
        var rows = TorrentDownloadsIndexer.Parse(Fixture("torrentdownloads", "coherence.xml"));

        rows.Should().HaveCount(50);
        rows.Should().OnlyContain(row => IndexerResultNormalizer.IsValidInfoHash(row.InfoHash));

        var first = rows[0];
        first.Title.Should().Be("Coherence (2013) (1080p BluRay x265 HEVC 10bit AAC 5 1 MONOLITH)");
        first.InfoHash.Should().Be("bd9c339c8105439135b8938633db632b4df51fbb");
        first.SizeBytes.Should().Be(4_093_671_584);
        first.Seeders.Should().Be(133);
        first.Leechers.Should().Be(9);
        first.DetailsUrl.Should().Be("https://www.torrentdownloads.pro/torrent/1667034954/Coherence-%282013%29-%281080p-BluRay-x265-HEVC-10bit-AAC-5-1-MONOLITH%29");
        first.PublishedUtc.Should().Be(new DateTimeOffset(2019, 8, 19, 18, 0, 16, TimeSpan.Zero));

        var geneMige = rows[1];
        geneMige.Title.Should().Be("Coherence 2013 1080p BluRay x264-GeneMige");
        geneMige.InfoHash.Should().Be("44a6b5a8a4cbb0f104e07332bb7e032328563649");
        geneMige.Seeders.Should().Be(130);
        geneMige.Leechers.Should().Be(58);

        var normalized = IndexerResultNormalizer.ToSearchResult(first, "torrentdownloads");
        normalized.FileUrl.Should().StartWith("magnet:?xt=urn:btih:bd9c339c8105439135b8938633db632b4df51fbb");
    }

    [Fact]
    public void TorrentDownloads_Empty_ReturnsNoRows()
    {
        TorrentDownloadsIndexer.Parse(Fixture("torrentdownloads", "empty.xml")).Should().BeEmpty();
    }

    [Fact]
    public void TorrentDownloads_NotAnRssFeed_Throws()
    {
        var act = () => TorrentDownloadsIndexer.Parse("<html><body><div class=\"grey_bar3\">x</div></body></html>");

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public async Task TorrentDownloads_SearchUsesRssWithPlusEncodedQuery()
    {
        var http = new FakeIndexerHttp(_ => Fixture("torrentdownloads", "coherence.xml"));

        var rows = await new TorrentDownloadsIndexer().SearchAsync(
            new IndexerQuery("Coherence 2013", MediaKind.Movie), http, CancellationToken.None);

        rows.Should().HaveCount(50);
        http.Requests.Should().ContainSingle()
            .Which.AbsoluteUri.Should().Be("https://www.torrentdownloads.pro/rss.xml?type=search&search=Coherence+2013");
    }

    /// <summary>Records requests; with a null responder any call fails the test.</summary>
    private sealed class FakeIndexerHttp(Func<Uri, string>? responder) : IIndexerHttp
    {
        public List<Uri> Requests { get; } = [];

        public Task<IndexerHttpResponse> GetAsync(Uri uri, CancellationToken cancellationToken)
        {
            if (responder is null)
            {
                throw new InvalidOperationException($"Unexpected HTTP request: {uri}");
            }

            Requests.Add(uri);
            return Task.FromResult(new IndexerHttpResponse(200, responder(uri), "application/json", uri));
        }

        public Task<IndexerHttpResponse> PostJsonAsync(Uri uri, string json, CancellationToken cancellationToken) =>
            throw new InvalidOperationException($"Unexpected HTTP POST: {uri}");
    }
}
