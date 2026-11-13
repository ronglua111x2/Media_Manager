using FluentAssertions;
using media_management_app.Services;

namespace MediaManager.Core.Tests.Search;

/// <summary>
/// native-search P1 / SRCH-13. Expectations come from real engine measurements
/// (docs/code-review/2026-11/07b-hunt-search.md §5c).
/// </summary>
public class TitleNormalizerTests
{
    [Theory]
    [InlineData("Léon: The Professional", "Leon: The Professional")]
    [InlineData("Amélie", "Amelie")]
    [InlineData("Đà Nẵng", "Da Nang")]
    [InlineData("Ærø Smørrebrød", "AEro Smorrebrod")]
    [InlineData("Straße", "Strasse")]
    [InlineData("Plain Title", "Plain Title")]
    public void FoldDiacritics_RemovesAccents(string input, string expected)
    {
        TitleNormalizer.FoldDiacritics(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("Schindler's List", "Schindlers List")]
    [InlineData("Schindler’s List", "Schindlers List")]
    [InlineData("Léon: The Professional", "Leon The Professional")]
    [InlineData("Spider-Man: No Way Home", "Spider-Man No Way Home")]
    [InlineData("Your Name.", "Your Name")]
    [InlineData("Sound! Euphonium", "Sound Euphonium")]
    public void ToSearchText_FoldsAndJoinsApostrophes(string input, string expected)
    {
        TitleNormalizer.ToSearchText(input).Should().Be(expected);
    }

    [Fact]
    public void QueryVariants_Apostrophe_SendsJoinedThenSplit()
    {
        // Measured: "Schindler s List" 413 rows / 7 engines, "Schindlers List" 361 / 5 engines;
        // kickass and sktorrent only matched the split form, limetorrents preferred the joined one.
        TitleNormalizer.QueryVariants("Schindler's List 1080p")
            .Should().Equal("Schindlers List 1080p", "Schindler s List 1080p");
    }

    [Fact]
    public void QueryVariants_Accent_SendsFoldedThenAccented()
    {
        // Measured: "Leon The Professional" 458 rows, "Léon The Professional" 207 (kickass only matched the accented one).
        TitleNormalizer.QueryVariants("Léon: The Professional")
            .Should().Equal("Leon The Professional", "Léon The Professional");
    }

    [Fact]
    public void QueryVariants_PlainTitle_HasSingleForm()
    {
        TitleNormalizer.QueryVariants("Coherence 2014").Should().Equal("Coherence 2014");
    }

    [Theory]
    [InlineData("Léon", "leon")]
    [InlineData("II", "2")]
    [InlineData("iii", "3")]
    [InlineData("IX", "9")]
    [InlineData("V", "v")]
    [InlineData("X", "x")]
    [InlineData("I", "i")]
    public void NormalizeToken_FoldsAndMapsSafeRomanNumerals(string token, string expected)
    {
        TitleNormalizer.NormalizeToken(token).Should().Be(expected);
    }

    [Fact]
    public void SignificantTitleTokens_MatchAcrossAccentsApostrophesAndRomanNumerals()
    {
        var expected = TitleNormalizer.SignificantTitleTokens("Léon: The Professional Part II");
        var release = TitleNormalizer.NormalizeTokens(TorrentCandidateParser.Tokenize("Leon.The.Professional.Part.2.1994.1080p"));

        expected.Should().OnlyContain(token => release.Contains(token));
    }

    [Fact]
    public void QueryTemplateRenderer_Normalize_AppendsExtraVariantsAfterAllPrimaryForms()
    {
        var queries = QueryTemplateRenderer.Normalize(["Schindler's List 1993", "Schindler's List"], sanitize: true);

        queries.Should().Equal("Schindlers List 1993", "Schindlers List", "Schindler s List 1993", "Schindler s List");
    }

    [Fact]
    public void QueryTemplateRenderer_Normalize_WithoutSanitize_KeepsRawText()
    {
        QueryTemplateRenderer.Normalize(["Léon: The Professional"], sanitize: false)
            .Should().Equal("Léon: The Professional");
    }
}
