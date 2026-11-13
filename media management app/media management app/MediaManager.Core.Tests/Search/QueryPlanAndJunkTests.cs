using FluentAssertions;
using media_management_app.Models;
using media_management_app.Search;
using media_management_app.Services;
using MediaManager.Core.Tests.Fixtures;

namespace MediaManager.Core.Tests.Search;

/// <summary>native-search P4 (targeted pack / anime queries) and P5 (junk filter, challenge detection).</summary>
public class QueryPlanAndJunkTests
{
    private readonly SearchPlanBuilder _planner = new(new SearchTitleResolver());

    [Fact]
    public void SeasonPackQueries_UseRecipeSeasonTemplatesPerSeason()
    {
        var recipe = RecipeBuilder.TvEpisode().Build();
        recipe.TargetKind = media_management_app.Common.MediaKind.TvSeasonPack;
        recipe.Modules.First(m => m.BlockType == RecipeBlockType.QueryBuilder).QueryTemplates =
            ["{title} S{season:00} complete {quality}", "{title} {episode} {quality}"];

        var queries = _planner.BuildSeasonPackQueries(recipe, TrackedShowBuilder.Show("Rick and Morty", year: 2013), [3, 4]);

        queries.Should().Contain("Rick and Morty S03 complete 1080p").And.Contain("Rick and Morty S04 complete 1080p");
        queries.Should().NotContain(query => query.Contains("{episode}") || query.EndsWith(" 1080p 1080p"));
    }

    [Fact]
    public void SeasonPackQueries_WithOnlyEpisodeTemplates_UseDefaultPackTemplates()
    {
        // "Common Anime Pack" only has {episode} templates, which are invalid for packs: the app's default
        // pack templates are used instead (they were silently dropped by the old snapshot path).
        var recipe = RecipeBuilder.TvEpisode().Build();
        recipe.TargetKind = media_management_app.Common.MediaKind.TvSeasonPack;
        recipe.Modules.First(m => m.BlockType == RecipeBlockType.QueryBuilder).QueryTemplates = ["{title} {episode} {quality}"];

        var queries = _planner.BuildSeasonPackQueries(recipe, TrackedShowBuilder.Show("Rick and Morty", year: 2013), [3]);

        queries.Should().Contain("Rick and Morty S03 complete 1080p").And.Contain("Rick and Morty season 3 1080p");
        queries.Should().OnlyContain(query => query.Contains("S03") || query.Contains("season 3"));
    }

    [Fact]
    public void AnimeEpisodeQueries_AddZeroPaddedEpisodeVariant()
    {
        var recipe = RecipeBuilder.TvEpisode().AnimeAbsolute().Build();
        recipe.Modules.First(m => m.BlockType == RecipeBlockType.QueryBuilder).QueryTemplates = ["{title} {episode} {quality}"];

        var queries = _planner.BuildEpisodeQueries(recipe, TrackedShowBuilder.Show("Kanata Kara", year: 2026), TrackedShowBuilder.Episode(season: 1, episode: 6));

        queries.Should().Equal("Kanata Kara 6 1080p", "Kanata Kara 06 1080p");
    }

    [Theory]
    [InlineData("Movie.2014.1080p.BluRay.x264.exe")]
    [InlineData("Movie 2014 1080p [codec pack required]")]
    [InlineData("Movie 2014 1080p KEYGEN")]
    [InlineData("Movie 2014 1080p password protected")]
    public void Junk_NamesAreRejected(string name)
    {
        var recipe = RecipeBuilder.Movie().Build();
        JunkFilter.GetJunkReason(recipe, TorrentResultBuilder.Magnet(name, fileSize: 2_000_000_000)).Should().NotBeNull();
    }

    [Fact]
    public void Junk_ImplausiblySmall1080pMovie_IsRejected_ButNormalSizesPass()
    {
        var recipe = RecipeBuilder.Movie().Build();

        JunkFilter.GetJunkReason(recipe, TorrentResultBuilder.Magnet("Movie 2014 1080p", fileSize: 120L * 1024 * 1024)).Should().NotBeNull();
        JunkFilter.GetJunkReason(recipe, TorrentResultBuilder.Magnet("Movie 2014 1080p", fileSize: 1_400_000_000)).Should().BeNull();
        JunkFilter.GetJunkReason(recipe, TorrentResultBuilder.Magnet("Movie 2014 1080p", fileSize: 0)).Should().BeNull();
    }

    // Audit 07c NS-10: "4K" marketing words and "kbps" no longer force the 2160p minimum; ".Bat." in a title is
    // not an executable.
    [Theory]
    [InlineData("Lawrence.of.Arabia.1962.4K.Remastered.1080p.BluRay.x265-GROUP", 900)]
    [InlineData("Coherence.2013.AAC.384kbps.DVDRip.720p.x264-GROUP", 700)]
    [InlineData("The.Bat.Whispers.1930.1080p.BluRay.x264-GROUP", 2000)]
    [InlineData("The.Bat.1959.SCR.720p.x264-GROUP", 900)]
    public void Junk_FalsePositives_AreNotRejected(string name, int sizeMb)
    {
        var recipe = RecipeBuilder.Movie().Build();

        JunkFilter.GetJunkReason(recipe, TorrentResultBuilder.Magnet(name, fileSize: sizeMb * 1024L * 1024)).Should().BeNull();
    }

    [Theory]
    [InlineData("Movie.2014.2160p.UHD.BluRay.x265-GROUP", "2160p")]
    [InlineData("Movie.2014.4K.HDR.x265-GROUP", "2160p")]
    [InlineData("Movie.2014.4K.Remastered.1080p.BluRay-GROUP", "1080p")]
    [InlineData("Movie.2014.AAC.384kbps.720p-GROUP", "720p")]
    [InlineData("Movie.2014.DVDRip.x264-GROUP", null)]
    [InlineData("Movie (2024) [3840x2160] [HEVC]", "2160p")]
    [InlineData("Movie (2024) [1920x1080] [x264]", "1080p")]
    [InlineData("Movie.2024.FHD.x264-GROUP", "1080p")]
    [InlineData("Movie 2024 1080 WEB", "1080p")]
    [InlineData("Movie.2024.2160p.4K.1080p.720p.WEB", "2160p")]
    public void Junk_ResolutionFromTokens(string name, string? expected)
    {
        JunkFilter.ResolutionFromTokens(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("Movie.2014.1080p.BluRay.x264.scr")]
    [InlineData("Movie.2014.1080p.BluRay.x264.bat")]
    [InlineData("Movie 2014 1080p setup.msi")]
    [InlineData("Rick and Morty S09E10 1080p WEB h264-EDITH scr")]
    [InlineData("Rick and Morty S09E10 1080p WEB h264-EDITH exe")]
    public void Junk_ExecutableExtensions_AreRejected(string name)
    {
        var recipe = RecipeBuilder.Movie().Build();

        JunkFilter.GetJunkReason(recipe, TorrentResultBuilder.Magnet(name, fileSize: 2_000_000_000)).Should().NotBeNull();
    }

    [Fact]
    public void Junk_ShortAnimeEpisode_IsNotRejected()
    {
        var recipe = RecipeBuilder.TvEpisode().AnimeAbsolute().Build();
        var result = TorrentResultBuilder.Magnet("[SubsPlease] Short Anime - 05 (1080p).mkv", fileSize: 60L * 1024 * 1024);

        JunkFilter.GetJunkReason(recipe, result).Should().BeNull();
    }

    [Fact]
    public void Evaluation_ReportsSuspiciousRelease()
    {
        var evaluator = new CandidateEvaluationService(new SearchTitleResolver());
        var result = evaluator.EvaluateMovie(
            RecipeBuilder.Movie().Build(),
            TrackedShowBuilder.Movie("Coherence", 2014),
            TorrentResultBuilder.Magnet("Coherence.2013.1080p.BluRay.x264.exe", fileSize: 1_500_000_000));

        result.RejectReason.Should().Be(CandidateRejectReason.SuspiciousRelease);
    }

    [Fact]
    public void ChallengeDetector_IgnoresRssWithPassiveCloudflareBeacon()
    {
        const string body = """<?xml version="1.0"?><rss><channel></channel></rss><script src="/cdn-cgi/challenge-platform/scripts/jsd/main.js"></script>""";

        ChallengeDetector.LooksLikeChallenge(body, "text/html").Should().BeFalse();
        ChallengeDetector.LooksLikeChallenge("<html><head><title>Just a moment...</title></head></html>", "text/html").Should().BeTrue();
    }
}

public class NativeQueryTextTests
{
    [Theory]
    [InlineData("Coherence 2014 1080p", media_management_app.Common.MediaKind.Movie, "Coherence")]
    [InlineData("Coherence 1080p", media_management_app.Common.MediaKind.Movie, "Coherence")]
    [InlineData("2001 A Space Odyssey 1968", media_management_app.Common.MediaKind.Movie, "2001 A Space Odyssey")]
    [InlineData("1917 2019 2160p", media_management_app.Common.MediaKind.Movie, "1917")]
    [InlineData("Rick and Morty S03E05 1080p", media_management_app.Common.MediaKind.TvEpisode, "Rick and Morty S03E05")]
    [InlineData("Rick and Morty 2013 season 3 1080p", media_management_app.Common.MediaKind.TvSeasonPack, "Rick and Morty season 3")]
    [InlineData("Kanata Kara 06 1080p", media_management_app.Common.MediaKind.TvEpisode, "Kanata Kara 06")]
    [InlineData("Dune 2021 1080p", media_management_app.Common.MediaKind.Movie, "Dune 2021")]
    [InlineData("Dune 1080p", media_management_app.Common.MediaKind.Movie, "Dune")]
    public void Reduce_DropsQualityAndMovieYears(string query, media_management_app.Common.MediaKind kind, string expected)
    {
        media_management_app.Search.NativeQueryText.Reduce(query, kind).Should().Be(expected);
    }
}

public class NativeSpecificQueryTests
{
    [Theory]
    [InlineData("Oppenheimer 2023 1080p", "Oppenheimer 1080p")]
    [InlineData("Oppenheimer 1080p", "Oppenheimer 1080p")]
    [InlineData("Oppenheimer 2023", null)]
    [InlineData("Oppenheimer", null)]
    public void Specific_KeepsQualityDropsMovieYear(string query, string? expected)
    {
        media_management_app.Search.NativeQueryText.Specific(query, media_management_app.Common.MediaKind.Movie).Should().Be(expected);
    }

    [Fact]
    public void YtsDownloadUrl_YieldsInfoHash()
    {
        CandidateDeduplicator.TryGetInfoHash("https://yts.mx/torrent/download/02A5AD1A0B1C2D3E4F5061728394A5B6C7D8E9F0")
            .Should().Be("02a5ad1a0b1c2d3e4f5061728394a5b6c7d8e9f0");
    }
}

public class NativeSpecificQueriesForRecipeTests
{
    [Fact]
    public void MovieWithoutQuality_GetsTopTwoRecipeQualities()
    {
        media_management_app.Search.NativeQueryText
            .SpecificQueries("Spider-Man No Way Home", media_management_app.Common.MediaKind.Movie, ["1080p", "2160p", "720p"])
            .Should().Equal("Spider-Man No Way Home 2160p", "Spider-Man No Way Home 1080p");
    }

    [Fact]
    public void EpisodeWithoutQuality_GetsNothingExtra()
    {
        media_management_app.Search.NativeQueryText
            .SpecificQueries("Show S01E05", media_management_app.Common.MediaKind.TvEpisode, ["1080p"])
            .Should().BeEmpty();
    }
}
