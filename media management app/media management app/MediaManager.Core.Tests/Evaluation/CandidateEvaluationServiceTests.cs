using FluentAssertions;
using media_management_app.Models;
using media_management_app.Services;
using MediaManager.Core.Tests.Fixtures;

namespace MediaManager.Core.Tests.Evaluation;

public class CandidateEvaluationServiceTests
{
    private const string JjkS03E01 =
        "[Subeteka] Jujutsu Kaisen - S03E01 [1080p WEB DUAL DDP2.0 H.265] [7BE3B471].mkv";

    private const string JjkS03E05 =
        "[Subeteka] Jujutsu Kaisen - S03E05 [1080p WEB DUAL DDP2.0 H.265] [6A8D1248].mkv";

    private const string TaiariAbsolute07 =
        "[Erai-raws] Tai Ari Deshita Ojou-sama wa Kakutou Game Nante Shinai - 07 [1080p CR WEB-DL AVC AAC][MultiSub][0FBE586A].mkv";

    private readonly FakeSearchTitleResolver _titles = new();
    private readonly CandidateEvaluationService _sut;

    public CandidateEvaluationServiceTests()
    {
        _sut = new CandidateEvaluationService(_titles);
    }

    [Fact]
    public void EvaluateEpisode_MatchingSxxExx_Accepts()
    {
        var golden = GoldenFixtureFile.Root("evaluation-releases.json")
            .GetProperty("fixtures")
            .EnumerateArray()
            .First(item => item.GetProperty("id").GetString() == "jjk-s03e01-accept")
            .GetProperty("fileName")
            .GetString();

        var result = EvaluateEpisode(golden!);

        result.IsAccepted.Should().BeTrue();
        result.RejectReason.Should().Be(CandidateRejectReason.None);
        result.TotalScore.Should().BeGreaterThan(0);
    }

    [Fact]
    public void EvaluateEpisode_WrongEpisode_RejectsEpisodeMismatch()
    {
        var result = EvaluateEpisode(JjkS03E05);

        result.IsAccepted.Should().BeFalse();
        result.RejectReason.Should().Be(CandidateRejectReason.EpisodeMismatch);
    }

    [Fact]
    public void EvaluateEpisode_AnimeAbsoluteMatch_Accepts()
    {
        _titles.Titles = ["Tai Ari Deshita"];
        var recipe = RecipeBuilder.TvEpisode().AnimeAbsolute().Build();
        var show = TrackedShowBuilder.Show("Tai Ari Deshita", year: 2025);
        var episode = TrackedShowBuilder.Episode(season: 1, episode: 7);

        var result = _sut.EvaluateEpisode(recipe, show, episode, TorrentResultBuilder.Magnet(TaiariAbsolute07));

        result.IsAccepted.Should().BeTrue();
        result.RejectReason.Should().Be(CandidateRejectReason.None);
    }

    [Fact]
    public void EvaluateEpisode_AnimeAbsoluteWrongNumber_RejectsEpisodeMismatch()
    {
        _titles.Titles = ["Tai Ari Deshita"];
        var recipe = RecipeBuilder.TvEpisode().AnimeAbsolute().Build();
        var show = TrackedShowBuilder.Show("Tai Ari Deshita", year: 2025);
        var episode = TrackedShowBuilder.Episode(season: 1, episode: 12);

        var result = _sut.EvaluateEpisode(recipe, show, episode, TorrentResultBuilder.Magnet(TaiariAbsolute07));

        result.RejectReason.Should().Be(CandidateRejectReason.EpisodeMismatch);
        result.RejectDetail.Should().Contain("absolute episode 12");
    }

    [Fact]
    public void EvaluateEpisode_YearMismatch_Rejects()
    {
        var show = TrackedShowBuilder.Show(year: 2018);
        var filename = "Jujutsu.Kaisen.2020.S03E01.1080p.WEB.mkv";

        var result = EvaluateEpisode(filename, show: show);

        result.RejectReason.Should().Be(CandidateRejectReason.YearMismatch);
    }

    [Fact]
    public void EvaluateEpisode_TitleMismatch_Rejects()
    {
        _titles.Titles = ["Completely Different Show"];

        var result = EvaluateEpisode(JjkS03E01);

        result.RejectReason.Should().Be(CandidateRejectReason.TitleMismatch);
    }

    [Fact]
    public void EvaluateEpisode_NotAddableLink_Rejects()
    {
        var result = EvaluateEpisode(new TorrentSearchResult
        {
            FileName = JjkS03E01,
            FileUrl = string.Empty,
            Seeders = 20,
            FileSize = 1_500_000_000
        });

        result.RejectReason.Should().Be(CandidateRejectReason.NotAddable);
    }

    [Fact]
    public void EvaluateEpisode_PluginErrorRow_Rejects()
    {
        var result = EvaluateEpisode("Jackett: API key error — right-click this row");

        result.RejectReason.Should().Be(CandidateRejectReason.PluginError);
    }

    [Fact]
    public void EvaluateEpisode_SeedersTooLow_Rejects()
    {
        var recipe = RecipeBuilder.TvEpisode().MinimumSeeders(50).Build();

        var result = EvaluateEpisode(JjkS03E01, recipe: recipe, seeders: 5);

        result.RejectReason.Should().Be(CandidateRejectReason.SeedersTooLow);
    }

    [Fact]
    public void EvaluateEpisode_QualityMismatch_Rejects()
    {
        var recipe = RecipeBuilder.TvEpisode().QualityAllowList("2160p").Build();

        var result = EvaluateEpisode(JjkS03E01, recipe: recipe);

        result.RejectReason.Should().Be(CandidateRejectReason.QualityMismatch);
    }

    [Fact]
    public void EvaluateEpisode_MissingIncludeTerm_Rejects()
    {
        var recipe = RecipeBuilder.TvEpisode().IncludeTerms("Dual-Audio").Build();

        var result = EvaluateEpisode(JjkS03E01, recipe: recipe);

        result.RejectReason.Should().Be(CandidateRejectReason.MissingIncludeTerm);
    }

    [Fact]
    public void EvaluateEpisode_ExcludedTerm_Rejects()
    {
        var recipe = RecipeBuilder.TvEpisode().ExcludeTerms("Subeteka").Build();

        var result = EvaluateEpisode(JjkS03E01, recipe: recipe);

        result.RejectReason.Should().Be(CandidateRejectReason.ExcludedTerm);
    }

    [Fact]
    public void EvaluateEpisode_BlockedReleaseGroup_Rejects()
    {
        var recipe = RecipeBuilder.TvEpisode().BlockedGroups("Subeteka").Build();

        var result = EvaluateEpisode(JjkS03E01, recipe: recipe);

        result.RejectReason.Should().Be(CandidateRejectReason.BlockedReleaseGroup);
    }

    [Fact]
    public void EvaluateEpisode_SeasonPack_RejectsWrongReleaseKind()
    {
        var pack =
            "[Judas] Hibike Euphonium (Sound! Euphonium) (Seasons 1-2 + Movies + Specials + OVA) [BD 1080p][HEVC x265 10bit][Eng-Subs]";

        var result = EvaluateEpisode(pack);

        result.RejectReason.Should().Be(CandidateRejectReason.WrongReleaseKind);
    }

    [Fact]
    public void EvaluateEpisode_SizeTooSmall_Rejects()
    {
        var recipe = RecipeBuilder.TvEpisode().SizeRange(2_000_000_000, null).Build();

        var result = EvaluateEpisode(JjkS03E01, recipe: recipe, fileSize: 500_000_000);

        result.RejectReason.Should().Be(CandidateRejectReason.SizeTooSmall);
    }

    [Fact]
    public void EvaluateMovie_MatchingYear_Accepts()
    {
        _titles.Titles = ["Cosmic Princess Kaguya"];
        var recipe = RecipeBuilder.Movie().Build();
        var movie = TrackedShowBuilder.Movie("Cosmic Princess Kaguya", 2026);
        var filename = "Cosmic.Princess.Kaguya.2026.1080p.NF.WEB-DL.MULTi.DDP5.1.H.264-VARYG.mkv";

        var result = _sut.EvaluateMovie(recipe, movie, TorrentResultBuilder.Magnet(filename));

        result.IsAccepted.Should().BeTrue();
        result.RejectReason.Should().Be(CandidateRejectReason.None);
    }

    [Fact]
    public void EvaluateMovie_MissingYear_RejectsYearMismatch()
    {
        _titles.Titles = ["Cosmic Princess Kaguya"];
        var recipe = RecipeBuilder.Movie().Build();
        var movie = TrackedShowBuilder.Movie("Cosmic Princess Kaguya", 2026);
        var filename = "Cosmic.Princess.Kaguya.1080p.NF.WEB-DL.mkv";

        var result = _sut.EvaluateMovie(recipe, movie, TorrentResultBuilder.Magnet(filename));

        result.RejectReason.Should().Be(CandidateRejectReason.YearMismatch);
    }

    private RecipeCandidateResult EvaluateEpisode(
        string fileName,
        SearchRecipe? recipe = null,
        TrackedShow? show = null,
        int seeders = 20,
        long fileSize = 1_500_000_000) =>
        EvaluateEpisode(TorrentResultBuilder.Magnet(fileName, seeders, fileSize), recipe, show);

    private RecipeCandidateResult EvaluateEpisode(
        TorrentSearchResult searchResult,
        SearchRecipe? recipe = null,
        TrackedShow? show = null)
    {
        recipe ??= RecipeBuilder.TvEpisode().Build();
        show ??= TrackedShowBuilder.Show();
        var episode = TrackedShowBuilder.Episode(3, 1);
        return _sut.EvaluateEpisode(recipe, show, episode, searchResult);
    }
}
