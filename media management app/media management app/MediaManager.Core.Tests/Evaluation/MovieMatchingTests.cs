using FluentAssertions;
using media_management_app.Models;
using media_management_app.Services;
using MediaManager.Core.Tests.Fixtures;

namespace MediaManager.Core.Tests.Evaluation;

/// <summary>
/// native-search P1: movie matching with the real title resolver (SRCH-11 year, SRCH-12 alternative
/// titles, SRCH-13 accents/apostrophes). Release names are real rows returned by public engines.
/// </summary>
public class MovieMatchingTests
{
    private readonly CandidateEvaluationService _sut = new(new SearchTitleResolver());

    private static SearchRecipe MovieRecipe(params string[] quality)
    {
        var recipe = RecipeBuilder.Movie().Build();
        var filter = recipe.Modules.First(module => module.BlockType == RecipeBlockType.CandidateFilter);
        filter.QualityAllowList = quality.Length > 0 ? [.. quality] : ["2160p", "1080p", "720p"];
        return recipe;
    }

    private RecipeCandidateResult Evaluate(TrackedMovie movie, string name, SearchRecipe? recipe = null) =>
        _sut.EvaluateMovie(recipe ?? MovieRecipe(), movie, TorrentResultBuilder.Magnet(name));

    [Theory]
    [InlineData("Coherence (2013) 1080p BrRip x264 - YIFY")]
    [InlineData("Coherence 2013 1080p BluRay x264-GeneMige")]
    public void Coherence_ReleasesTaggedWithFestivalYear_AreAccepted(string name)
    {
        var result = Evaluate(TrackedShowBuilder.Movie("Coherence", 2014), name);

        result.IsAccepted.Should().BeTrue(result.RejectDetail);
    }

    [Fact]
    public void ExactYear_RanksAboveOffByOne()
    {
        var movie = TrackedShowBuilder.Movie("Coherence", 2014);

        var exact = Evaluate(movie, "Coherence 2014 1080p BluRay x264-GROUP");
        var offByOne = Evaluate(movie, "Coherence 2013 1080p BluRay x264-GROUP");

        exact.IdentityScore.Should().BeGreaterThan(offByOne.IdentityScore);
    }

    [Fact]
    public void DistantYear_IsRejectedAsYearMismatch()
    {
        var result = Evaluate(TrackedShowBuilder.Movie("Dune", 1984), "Dune.2021.1080p.WEB-DL.DDP5.1.Atmos-GROUP");

        result.RejectReason.Should().Be(CandidateRejectReason.YearMismatch);
    }

    [Fact]
    public void NoYear_WithCompleteTitle_IsAccepted()
    {
        var result = Evaluate(TrackedShowBuilder.Movie("Coherence", 2014), "Coherence.1080p.BluRay.x264-GROUP");

        result.IsAccepted.Should().BeTrue(result.RejectDetail);
    }

    [Fact]
    public void NoYear_WithPartialTitle_IsRejected()
    {
        var result = Evaluate(
            TrackedShowBuilder.Movie("The Lord of the Rings The Return of the King", 2003),
            "The.Lord.of.the.Rings.1080p.BluRay.x264-GROUP");

        result.IsAccepted.Should().BeFalse();
    }

    [Theory]
    [InlineData("Leon The Professional 1994 REMASTERED EXTENDED 1080p BluRay x265-RARBG")]
    [InlineData("Léon.The.Professional.1994.REMASTERED.EXTENDED.1080p.BluRay.x265 -88")]
    public void AccentedTitle_MatchesBothSpellings(string name)
    {
        Evaluate(TrackedShowBuilder.Movie("Léon: The Professional", 1994), name).IsAccepted.Should().BeTrue();
    }

    [Fact]
    public void SingleWordAccentedTitle_MatchesFoldedRelease()
    {
        Evaluate(TrackedShowBuilder.Movie("Amélie", 2001), "Amelie.2001.1080p.BluRay.x264-GROUP").IsAccepted.Should().BeTrue();
    }

    [Theory]
    [InlineData("Schindlers.List.1993.1080p.BluRay.x265-RARBM")]
    [InlineData("Schindlers List 1993 1080p BluRay x264-OFT")]
    public void ApostropheTitle_MatchesJoinedRelease(string name)
    {
        Evaluate(TrackedShowBuilder.Movie("Schindler's List", 1993), name).IsAccepted.Should().BeTrue();
    }

    [Fact]
    public void NumberInTitle_IsNotTreatedAsYear()
    {
        var result = Evaluate(
            TrackedShowBuilder.Movie("2001: A Space Odyssey", 1968),
            "2001 A Space Odyssey 1968 Remastered 1080p BluRay HEVC x265 5 1 BONE");

        result.IsAccepted.Should().BeTrue(result.RejectDetail);
    }

    [Fact]
    public void RomajiAlternativeTitle_IsAcceptedEvenWhenRecipeDoesNotSearchIt()
    {
        // Liz and the Blue Bird has "Liz to Aoi Tori" in the library; releases often carry only the romaji.
        var recipe = MovieRecipe();
        recipe.Modules.First(module => module.BlockType == RecipeBlockType.Identity)
            .ExtensionData[RecipeRuntimeSettings.UseLibraryEnglishTitlesKey] = bool.FalseString;
        var movie = TrackedShowBuilder.Movie("Liz and the Blue Bird", 2018, "Liz to Aoi Tori", "Rizu to aoi tori");

        var result = Evaluate(movie, "[Group] Liz to Aoi Tori (2018) [BD 1080p HEVC FLAC]", recipe);

        result.IsAccepted.Should().BeTrue(result.RejectDetail);
    }

    [Fact]
    public void NumberedMovieTitle_DoesNotAcceptYearlessEpisodeOfTheSeries()
    {
        var movie = TrackedShowBuilder.Movie("Jujutsu Kaisen 0", 2021);

        Evaluate(movie, "[Kaizoku] Jujutsu Kaisen - 53 (WEB 1080p HEVC EAC-3) [B87CF501]").IsAccepted.Should().BeFalse();
        Evaluate(movie, "[Group] Jujutsu Kaisen 0 (BD 1080p HEVC)").IsAccepted.Should().BeTrue();
    }

    [Fact]
    public void UnrelatedTitle_IsStillRejected()
    {
        var result = Evaluate(TrackedShowBuilder.Movie("Coherence", 2014), "Predestination.2014.1080p.BluRay.x264-GROUP");

        result.RejectReason.Should().Be(CandidateRejectReason.TitleMismatch);
    }
    // Audit 07c NS-01: one year off plus a loose title match accepted the neighbouring film of a franchise.
    [Theory]
    [InlineData("Kill Bill: Vol. 2", 2004, "Kill.Bill.Vol.1.2003.1080p.BluRay.x264-GROUP")]
    [InlineData("Harry Potter and the Deathly Hallows: Part 1", 2010, "Harry.Potter.and.the.Deathly.Hallows.Part.2.2011.1080p.BluRay.x264-GROUP")]
    [InlineData("Detective Conan: The Million-dollar Pentagram", 2024, "Detective.Conan.The.Black.Iron.Submarine.2023.1080p.BluRay.x264-GROUP")]
    public void OffByOneYear_OtherFranchiseEntry_IsRejected(string title, int year, string name)
    {
        Evaluate(TrackedShowBuilder.Movie(title, year), name).IsAccepted.Should().BeFalse();
    }

    [Fact]
    public void OffByOneYear_ShortRomajiAlternative_DoesNotMatchAnotherFilm()
    {
        var movie = TrackedShowBuilder.Movie("Your Name.", 2016, "Kimi no Na wa.");

        Evaluate(movie, "[Group] Kimi no Koe wo Todoketai (2017) [BD 1080p HEVC FLAC]").IsAccepted.Should().BeFalse();
        Evaluate(movie, "Kimi no Na wa. (2017) [BD 1080p HEVC FLAC]").IsAccepted.Should().BeTrue();
    }

    [Theory]
    [InlineData("Kill Bill: Vol. 2", 2004, "Kill.Bill.Vol.2.2003.1080p.BluRay.x264-GROUP")]
    [InlineData("Your Name.", 2016, "Your.Name.2017.1080p.BluRay.x264-GROUP")]
    public void OffByOneYear_CompleteTitle_IsAccepted(string title, int year, string name)
    {
        var result = Evaluate(TrackedShowBuilder.Movie(title, year), name);

        result.IsAccepted.Should().BeTrue(result.RejectDetail);
    }

    // Audit 07c NS-02: a release without a year was accepted when it merely started with the title.
    [Theory]
    [InlineData("Dune", 2021, "Dune.Part.Two.1080p.WEB-DL.DDP5.1.Atmos-GROUP")]
    [InlineData("Frozen", 2013, "Frozen.II.1080p.BluRay.x264-GROUP")]
    [InlineData("Frozen", 2013, "Frozen.Fever.1080p.BluRay.x264-GROUP")]
    [InlineData("Harry Potter and the Deathly Hallows: Part 1", 2010, "Harry.Potter.and.the.Deathly.Hallows.Part.2.1080p.BluRay.DD5.1.x264-GROUP")]
    public void NoYear_SequelOrSpinOff_IsRejected(string title, int year, string name)
    {
        Evaluate(TrackedShowBuilder.Movie(title, year), name).IsAccepted.Should().BeFalse();
    }

    [Fact]
    public void NoYear_ShortRomajiAlternative_DoesNotMatchAnotherFilm()
    {
        var movie = TrackedShowBuilder.Movie("Your Name.", 2016, "Kimi no Na wa.");

        Evaluate(movie, "[Group] Kimi no Iro [BD 1080p HEVC FLAC]").IsAccepted.Should().BeFalse();
    }

    [Theory]
    [InlineData("Dune", 2021, "Dune.1080p.WEB-DL.DDP5.1.Atmos-GROUP")]
    [InlineData("Dune", 2021, "Dune.Extended.Cut.1080p.WEB-DL-GROUP")]
    [InlineData("Liz and the Blue Bird", 2018, "[MTBB] Liz and the Blue Bird (BD 1080p) | Liz to Aoi Tori")]
    [InlineData("Jujutsu Kaisen 0", 2021, "[Group] Jujutsu Kaisen 0 The Movie (BD 1080p HEVC)")]
    public void NoYear_SameFilmWithEditionWords_IsAccepted(string title, int year, string name)
    {
        var result = Evaluate(TrackedShowBuilder.Movie(title, year), name);

        result.IsAccepted.Should().BeTrue(result.RejectDetail);
    }
}
