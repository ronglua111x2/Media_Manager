using FluentAssertions;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Search;
using media_management_app.Search.Indexers;
using media_management_app.Services;
using MediaManager.Core.Tests.Fixtures;

namespace MediaManager.Core.Tests.Search;

/// <summary>Audit 07c batch 2: NS-12 (v2 revisions), NS-14 (SubsPlease query), NS-21 (unknown seeders), NS-28, NS-29.</summary>
public class Batch2FetchTests
{
    private static string Fixture(string indexer, string file) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Indexers", indexer, file));

    [Fact]
    public void AnimeAbsoluteEpisode_WithV2Revision_IsAccepted()
    {
        var evaluator = new CandidateEvaluationService(new SearchTitleResolver());
        var recipe = RecipeBuilder.TvEpisode().AnimeAbsolute().Build();
        var show = TrackedShowBuilder.Show("Kanata Kara", year: 2026);

        var result = evaluator.EvaluateEpisode(recipe, show, TrackedShowBuilder.Episode(season: 1, episode: 6),
            TorrentResultBuilder.Magnet("[SubsPlease] Kanata kara - 06v2 (1080p) [ABCD1234].mkv", fileSize: 1_400_000_000));

        result.IsAccepted.Should().BeTrue(result.RejectDetail);
    }

    [Fact]
    public void SeasonEpisode_WithV2Revision_IsAccepted()
    {
        var evaluator = new CandidateEvaluationService(new SearchTitleResolver());
        var recipe = RecipeBuilder.TvEpisode().Build();
        var show = TrackedShowBuilder.Show("Rick and Morty", year: 2013);

        var result = evaluator.EvaluateEpisode(recipe, show, TrackedShowBuilder.Episode(season: 3, episode: 6),
            TorrentResultBuilder.Magnet("Rick.and.Morty.S03E06v2.1080p.WEB.x264-GROUP", fileSize: 900_000_000));

        result.IsAccepted.Should().BeTrue(result.RejectDetail);
    }

    [Theory]
    [InlineData("Kanata Kara S01E06", true, "Kanata Kara")]
    [InlineData("Kanata Kara 06", true, "Kanata Kara")]
    [InlineData("Kanata Kara - 06 1080p", true, "Kanata Kara")]
    [InlineData("Kanata Kara S01 complete", false, "Kanata Kara")]
    [InlineData("Mob Psycho 100 S01E05", true, "Mob Psycho 100")]
    [InlineData("Mob Psycho 100 05", true, "Mob Psycho 100")]
    [InlineData("Mob Psycho 100 season 1", false, "Mob Psycho 100")]
    [InlineData("86 S01E05 1080p", true, "86")]
    public void SubsPlease_SearchesTheShowName(string query, bool episodeQuery, string expected)
    {
        SubsPleaseIndexer.ShowQuery(query, episodeQuery).Should().Be(expected);
    }

    [Fact]
    public void UnknownSeeders_PassTheMinimumSeederFilter_ButKnownZeroDoesNot()
    {
        var recipe = RecipeBuilder.TvEpisode().Build();
        recipe.Modules.First(module => module.BlockType == RecipeBlockType.CandidateFilter).MinimumSeeders = 3;
        var unknown = new TorrentSearchResult { FileName = "[SubsPlease] Show - 05 (1080p).mkv", FileUrl = "magnet:?xt=urn:btih:bd9c339c8105439135b8938633db632b4df51fbb", Seeders = 0, SeedersUnknown = true };
        var dead = new TorrentSearchResult { FileName = "[SubsPlease] Show - 05 (1080p).mkv", FileUrl = "magnet:?xt=urn:btih:bd9c339c8105439135b8938633db632b4df51fbb", Seeders = 0 };

        RecipeCandidateFilter.GetRejectReason(recipe, unknown).Reason.Should().NotBe(CandidateRejectReason.SeedersTooLow);
        RecipeCandidateFilter.GetRejectReason(recipe, dead).Reason.Should().Be(CandidateRejectReason.SeedersTooLow);
    }

    private sealed class FakeIndexer(string id, IndexerResult row) : ITorrentIndexer
    {
        public string Id => id;

        public string EngineName => id;

        public IndexerCapabilities Capabilities { get; } = new(IndexerMediaKinds.All);

        public Task<IReadOnlyList<IndexerResult>> SearchAsync(IndexerQuery query, IIndexerHttp http, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IndexerResult>>([row]);
    }

    private sealed class NoHttp : IIndexerHttp
    {
        public Task<IndexerHttpResponse> GetAsync(Uri uri, CancellationToken cancellationToken) => throw new InvalidOperationException();

        public Task<IndexerHttpResponse> PostJsonAsync(Uri uri, string json, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }

    [Fact]
    public async Task Merge_PrefersKnownSeeders_AndKeepsAKnownSize()
    {
        const string hash = "bd9c339c8105439135b8938633db632b4df51fbb";
        var noSwarm = new IndexerResult("[SubsPlease] Show - 05 (1080p).mkv", 1_400_000_000, 0, 0, hash, null, null, null, null, "subsplease", SeedersUnknown: true);
        var noSize = new IndexerResult("[SubsPlease] Show - 05 (1080p).mkv", 0, 120, 3, hash, null, null, null, null, "nyaa");
        var governor = new IndexerRequestGovernor(TimeProvider.System, _ => new IndexerPolicy(1000, 4, TimeSpan.Zero, 1000));
        var orchestrator = new SearchOrchestrator(governor, (_, _, _) => new NoHttp());

        var outcome = await orchestrator.SearchAsync(
            [new IndexerQuery("Show 05", MediaKind.TvEpisode, IsAnime: true)],
            [new FakeIndexer("subsplease", noSwarm), new FakeIndexer("nyaa", noSize)],
            SearchLane.Interactive, SearchBudget.Interactive, CancellationToken.None);

        var row = outcome.Results.Should().ContainSingle().Subject;
        row.Seeders.Should().Be(120);
        row.SeedersUnknown.Should().BeFalse();
        row.FileSize.Should().Be(1_400_000_000);
    }

    [Fact]
    public void FuzzyDedupe_DoesNotChainSizesBeyondTheTolerance()
    {
        var mb = 1024L * 1024;
        (string Name, long Size, int Seeds, string Url)[] rows =
        [
            ("Show.S01E01.1080p.WEB.x264-GROUP", 1000 * mb, 5, "magnet:?xt=urn:btih:1111111111111111111111111111111111111111"),
            ("Show.S01E01.1080p.WEB.x264-GROUP", 1004 * mb, 9, "magnet:?xt=urn:btih:2222222222222222222222222222222222222222"),
            ("Show.S01E01.1080p.WEB.x264-GROUP", 1008 * mb, 3, "magnet:?xt=urn:btih:3333333333333333333333333333333333333333")
        ];

        var kept = CandidateDeduplicator.Deduplicate(rows, r => r.Name, r => r.Size, r => r.Url, r => r.Seeds, fuzzy: true, sizeToleranceMb: 5);

        kept.Should().HaveCount(2);
    }

    [Fact]
    public void TsukiHime_SkipsAdultRows()
    {
        var json = Fixture("tsukihime", "kanata-kara.json");
        var all = TsukiHimeIndexer.Parse(json).Count;
        var index = json.IndexOf("\"is_adult\":0", StringComparison.Ordinal);
        var withAdult = json[..index] + "\"is_adult\":1" + json[(index + "\"is_adult\":0".Length)..];

        TsukiHimeIndexer.Parse(withAdult).Should().HaveCount(all - 1);
    }
    // NS-13: a page with results that no longer parse is a parse error (breaker), not "no results".
    [Fact]
    public void LimeTorrents_ChangedRowMarkup_Throws_EmptyPageDoesNot()
    {
        var page = Fixture("limetorrents", "coherence.html").Replace("<a href=\"http://itorrents", "<a href=\"//itorrents");

        var act = () => LimeTorrentsIndexer.Parse(page);

        act.Should().Throw<FormatException>();
        LimeTorrentsIndexer.Parse(Fixture("limetorrents", "empty.html")).Should().BeEmpty();
    }

    [Fact]
    public void Apibay_RenamedHashField_Throws_NoResultsRowDoesNot()
    {
        var renamed = Fixture("apibay", "coherence.json").Replace("\"info_hash\"", "\"infohash_v2\"");

        var act = () => ApibayIndexer.Parse(renamed);

        act.Should().Throw<FormatException>();
        ApibayIndexer.Parse(Fixture("apibay", "empty.json")).Should().BeEmpty();
    }
    // NS-18: only the movie's own year leaves the query; years that are part of the title stay.
    [Theory]
    [InlineData("Blade Runner 2049 2017 1080p", 2017, "Blade Runner 2049")]
    [InlineData("Blade Runner 2049 1080p", 2017, "Blade Runner 2049")]
    [InlineData("Wonder Woman 1984 2020", 2020, "Wonder Woman 1984")]
    [InlineData("Godzilla 2000 1999", 1999, "Godzilla 2000")]
    [InlineData("Coherence 2013", 2014, "Coherence")]
    public void Reduce_KeepsYearsThatArePartOfTheTitle(string query, int year, string expected)
    {
        NativeQueryText.Reduce(query, MediaKind.Movie, year).Should().Be(expected);
    }

    // NS-11: title numbers that look like years.
    [Theory]
    [InlineData("1917", 2019, "1917.2019.1080p.BluRay.x264-SPARKS")]
    [InlineData("1917", 2019, "1917 (2019) [1080p] [YTS.MX]")]
    [InlineData("Wonder Woman 1984", 2020, "Wonder.Woman.1984.2020.1080p.WEB-DL.x264-GROUP")]
    [InlineData("Wonder Woman 1984", 2020, "Wonder.Woman.1984.1080p.WEBRip.x264-GROUP")]
    public void MovieTitledWithAYear_IsAccepted(string title, int year, string name)
    {
        var evaluator = new CandidateEvaluationService(new SearchTitleResolver());

        var result = evaluator.EvaluateMovie(RecipeBuilder.Movie().Build(), TrackedShowBuilder.Movie(title, year), TorrentResultBuilder.Magnet(name, fileSize: 2_000_000_000));

        result.IsAccepted.Should().BeTrue(result.RejectDetail);
    }

    // NS-19: a title without a word longer than two letters must be the whole release title.
    [Fact]
    public void ShortTitle_OnlyMatchesItself()
    {
        var evaluator = new CandidateEvaluationService(new SearchTitleResolver());
        var movie = TrackedShowBuilder.Movie("Us", 2019);
        var recipe = RecipeBuilder.Movie().Build();

        evaluator.EvaluateMovie(recipe, movie, TorrentResultBuilder.Magnet("Joker.2019.1080p.BluRay.x264-GROUP", fileSize: 2_000_000_000)).IsAccepted.Should().BeFalse();
        evaluator.EvaluateMovie(recipe, movie, TorrentResultBuilder.Magnet("Us.2019.1080p.BluRay.x264-GROUP", fileSize: 2_000_000_000)).IsAccepted.Should().BeTrue();
    }
}
