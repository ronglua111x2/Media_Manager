using FluentAssertions;
using media_management_app.Services;

namespace MediaManager.Core.Tests.Pack;

public class PackEpisodePatternInferrerTests
{
    [Fact]
    public void NormalizeStem_StripsRevisionTag()
    {
        PackEpisodePatternInferrer.NormalizeStem("Show Name - 01 v2")
            .Should().Be("Show Name - 01");
    }

    [Fact]
    public void NormalizeStem_StripsCrcHash()
    {
        PackEpisodePatternInferrer.NormalizeStem("Show Name - 01 [A1B2C3D4]")
            .Should().Be("Show Name - 01");
    }

    [Fact]
    public void NormalizeStem_StripsRepack()
    {
        PackEpisodePatternInferrer.NormalizeStem("Show Name - 02 REPACK")
            .Should().Be("Show Name - 02");
    }

    [Fact]
    public void Infer_FixedWidthTwoDigit_IsValid()
    {
        var stems = GoldenStems("fixed-width-two-digit");

        var pattern = PackEpisodePatternInferrer.Infer(stems);

        pattern.IsValid.Should().BeTrue();
        pattern.TryExtractFixedWidth(stems[1]).Should().Be(2);
    }

    [Fact]
    public void Infer_CompactSxxExxSuffix_UsesCompactSeason()
    {
        var stems = new[] { "Show Name S0101", "Show Name S0102", "Show Name S0103" };

        var pattern = PackEpisodePatternInferrer.Infer(stems, seasonNumber: 1);

        pattern.IsValid.Should().BeTrue();
        pattern.CompactSeasonNumber.Should().Be(1);
        PackEpisodePatternInferrer.TryExtract("Show Name S0102", pattern, 1).Should().Be(2);
    }

    [Fact]
    public void TryExtractSeasonEpisodeSuffix_MatchingSeason_ReturnsEpisode()
    {
        PackEpisodePatternInferrer.TryExtractSeasonEpisodeSuffix("Show Name S0105", 1)
            .Should().Be(5);
    }

    [Fact]
    public void TryExtractSeasonEpisodeSuffix_WrongSeason_ReturnsNull()
    {
        PackEpisodePatternInferrer.TryExtractSeasonEpisodeSuffix("Show Name S02E05", 1)
            .Should().BeNull();
    }

    [Fact]
    public void TryInferEpisode_ValidSet_ReturnsEpisode()
    {
        var valid = new HashSet<int> { 1, 2, 3, 4, 5 };
        var pattern = PackEpisodePatternInferrer.Infer(["Show - 01", "Show - 02", "Show - 03"]);

        var episode = PackEpisodePatternInferrer.TryInferEpisode("Show - 02", 1, valid, pattern);

        episode.Should().Be(2);
    }

    [Fact]
    public void TryInferEpisode_NotInValidSet_ReturnsNull()
    {
        var valid = new HashSet<int> { 1, 2, 3 };
        var pattern = PackEpisodePatternInferrer.Infer(["Show - 01", "Show - 02", "Show - 03"]);

        PackEpisodePatternInferrer.TryInferEpisode("Show - 09", 1, valid, pattern)
            .Should().BeNull();
    }

    [Fact]
    public void MapEpisodes_MapsEachStem()
    {
        var stems = new[] { "Show - 01", "Show - 02" };
        var pattern = PackEpisodePatternInferrer.Infer(stems);

        var mapped = PackEpisodePatternInferrer.MapEpisodes(stems, pattern);

        mapped["Show - 01"].Should().Be(1);
        mapped["Show - 02"].Should().Be(2);
    }

    [Fact]
    public void Infer_EmptyStems_IsInvalid()
    {
        PackEpisodePatternInferrer.Infer([]).IsValid.Should().BeFalse();
    }

    [Fact]
    public void TryExtractSeasonFromPrefix_ReadsSxx()
    {
        var stems = new[]
        {
            "Show Name S01 - 01 extra",
            "Show Name S01 - 02 extra"
        };
        var pattern = PackEpisodePatternInferrer.Infer(stems);

        var season = PackEpisodePatternInferrer.TryExtractSeasonFromPrefix("Show Name S01 - 01 extra", pattern);

        season.Should().Be(1);
    }

    [Fact]
    public void TryInferEpisode_StandardSxxExx_UsesRegexFallback()
    {
        var valid = new HashSet<int> { 4 };

        var episode = PackEpisodePatternInferrer.TryInferEpisode(
            "Show Name S03E04 1080p",
            seasonNumber: 3,
            validEpisodeNumbers: valid);

        episode.Should().Be(4);
    }

    // PARSE-01: crossing the 09 -> 10 boundary used to pick a prefix that read only the last digit.
    [Fact]
    public void Infer_DashNumbersAcrossTwoDigitBoundary_ReadsWholeNumber()
    {
        var stems = Enumerable.Range(1, 12).Select(e => $"Show Name - {e:00} (BD 1080p HEVC Opus) [Grp]").ToList();

        var pattern = PackEpisodePatternInferrer.Infer(stems, expectedEpisodeCount: 12, seasonNumber: 1);

        pattern.IsValid.Should().BeTrue();
        stems.Select(stem => PackEpisodePatternInferrer.TryExtract(stem, pattern, 1))
            .Should().Equal(Enumerable.Range(1, 12).Select(e => (int?)e));
    }

    [Fact]
    public void Infer_PatternFailingValidation_IsInvalid()
    {
        // Numbers 13-24 for a 12-episode season: any fixed-width pattern reads numbers outside 1..14.
        var stems = Enumerable.Range(13, 12).Select(e => $"[Group] Show - {e:00} [1080p]").ToList();

        var pattern = PackEpisodePatternInferrer.Infer(stems, expectedEpisodeCount: 12, seasonNumber: 2);

        pattern.IsValid.Should().BeFalse();
    }

    private static string[] GoldenStems(string id) =>
        MediaManager.Core.Tests.Fixtures.GoldenFixtureFile.Root("pack-stems.json")
            .GetProperty("cases")
            .EnumerateArray()
            .First(item => item.GetProperty("id").GetString() == id)
            .GetProperty("stems")
            .EnumerateArray()
            .Select(item => item.GetString()!)
            .ToArray();
}
