using MediaManager.App.Tests.Special.Fixtures;
using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.App.Tests.Special;

/// <summary>
/// The deterministic special/OVA tier (no Gemini): the season air-date window mapper, the OVA-number rule, the
/// title matcher and the review requirement (PARSE-09, PARSE-18, PARSE-19; report 10.3 step 1 and 10.4).
/// </summary>
public class SpecialRuleMappingTests
{
    [Fact(Skip = "PARSE-09")]
    public async Task TwoSeasonPack_RulesOnly_SeasonOneResolvedSeasonTwoLeftForReview()
    {
        var pack = SpecialCases.TwoSeasonBrassPack();
        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."));

        var result = await host.ResolveAsync(pack, useGemini: false);

        var mapped = Mapped(result);
        for (var index = 1; index <= 7; index++)
        {
            Assert.Equal(index, mapped[$"{SpecialCases.BrassS1}/Specials/[Grp] Harbor Brass - S1S{index:00}.mkv"]);
        }

        Assert.Equal(8, mapped[SpecialCases.BrassOva]);
        Assert.DoesNotContain(mapped.Keys, path => path.StartsWith(SpecialCases.BrassS2, StringComparison.Ordinal));
        Assert.Equal(0, host.Gemini.Calls);
    }

    /// <summary>Report 10.4: the strict tier never auto-applies a wrong pair on any of the eight layouts.</summary>
    [Theory(Skip = "PARSE-09")]
    [MemberData(nameof(SpecialCases.GeneralisationNames), MemberType = typeof(SpecialCases))]
    public async Task Generalisation_RulesOnly_NeverMapsAFileToTheWrongSpecial(string name)
    {
        var pack = SpecialCases.ByName(name);
        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."));

        var result = await host.ResolveAsync(pack, useGemini: false);

        foreach (var (path, episode) in Mapped(result))
        {
            Assert.True(
                pack.ExpectedS00E.TryGetValue(path, out var expected) && expected == episode,
                $"{name}: '{path}' mapped to S00E{episode:00}, expected S00E{expected:00}.");
        }
    }

    /// <summary>Layouts the strict tier must resolve completely (report 10.4, column "Tầng 1 chặt").</summary>
    [Theory(Skip = "PARSE-09")]
    [InlineData("archive-s1-ovas")]
    [InlineData("club-ova-per-season")]
    public async Task Generalisation_RulesOnly_ExactCountBlocksResolveCompletely(string name)
    {
        var pack = SpecialCases.ByName(name);
        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."));

        var result = await host.ResolveAsync(pack, useGemini: false);

        Assert.Equal(pack.ExpectedS00E.Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value!.Value), Mapped(result));
    }

    /// <summary>
    /// 32 numbered minis, 7 before season 1: one block of 32 for 32 files. Needs the numbers read correctly
    /// ("Mini Anime 10" is 10, not 0), which PARSE-17 fixes in the pattern inferrer.
    /// </summary>
    [Fact(Skip = "PARSE-17")]
    public async Task CompleteMiniSet_RulesOnly_AllThirtyTwoResolve()
    {
        var pack = SpecialCases.ByName("rooftop-complete-minis");
        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."));

        var result = await host.ResolveAsync(pack, useGemini: false);

        Assert.Equal(pack.ExpectedS00E.ToDictionary(pair => pair.Key, pair => pair.Value!.Value), Mapped(result));
    }

    /// <summary>Scenario I: a recap aired before season 1 is not in the pack, so season 1's block is not exact.</summary>
    [Fact(Skip = "PARSE-09")]
    public async Task RecapBeforeSeasonOne_RulesOnly_OnlyTheExactBlockResolves()
    {
        var pack = SpecialCases.RecapBeforeSeasonOne();
        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."));

        var result = await host.ResolveAsync(pack, useGemini: false);

        Assert.Equal(new Dictionary<string, int> { ["Show/Season 2/Show.S02S01.mkv"] = 3 }, Mapped(result));
    }

    [Fact(Skip = "PARSE-09")]
    public async Task Prepare_RulesOnlyMapping_RequiresReview()
    {
        var pack = SpecialCases.TwoSeasonBrassPack();
        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."), geminiEnabled: false);
        var service = SpecialPackPrepareHost.Create(pack, host.Orchestrator);

        var preview = await service.PrepareSeasonPackLinkAsync(SpecialPackPrepareHost.ShowId, 1, useGeminiForSpecials: false);

        Assert.True(preview.RequiresReview);
    }

    [Fact(Skip = "PARSE-09")]
    public async Task Prepare_DirectS00EWithoutTitle_RequiresReview()
    {
        var pack = DirectPack("Quiet Club/Specials/Quiet Club - S00E02.mkv");
        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."), geminiEnabled: false);
        var service = SpecialPackPrepareHost.Create(pack, host.Orchestrator);

        var preview = await service.PrepareSeasonPackLinkAsync(SpecialPackPrepareHost.ShowId, 1, useGeminiForSpecials: false);

        Assert.Equal(2, preview.Inventory.Files.Single(file => file.FileName == "Quiet Club - S00E02.mkv").MatchedEpisodeNumber);
        Assert.True(preview.RequiresReview);
    }

    [Fact]
    public async Task Prepare_DirectS00EWithMatchingTitle_LinksWithoutReview()
    {
        var pack = DirectPack("Quiet Club/Specials/Quiet Club - S00E02 - The Gift Exchange.mkv");
        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."), geminiEnabled: false);
        var service = SpecialPackPrepareHost.Create(pack, host.Orchestrator);

        var preview = await service.PrepareSeasonPackLinkAsync(SpecialPackPrepareHost.ShowId, 1, useGeminiForSpecials: false);

        Assert.Equal(2, preview.Inventory.Files.Single(file => file.FileName.Contains("S00E02")).MatchedEpisodeNumber);
        Assert.False(preview.RequiresReview);
    }

    /// <summary>SPEC-02: "OVA - 01" is the first OVA, not TMDB S00E01 (here a recap).</summary>
    [Fact(Skip = "PARSE-18")]
    public async Task OvaDash_NumberIsNotTheTmdbEpisode_NotMapped()
    {
        var pack = OvaDashPack(("Recap Special", "Promotional Video"));
        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."));

        var result = await host.ResolveAsync(pack, useGemini: false);

        Assert.Empty(Mapped(result));
    }

    [Fact]
    public async Task OvaDash_TmdbTitleNamesTheSameOva_Mapped()
    {
        var pack = OvaDashPack(("OVA 1: Hot Spring", "OVA 2: Beach Day"));
        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."));

        var result = await host.ResolveAsync(pack, useGemini: false);

        Assert.Equal(1, Mapped(result)["Lantern Knight [Grp]/Specials/[Grp] Lantern Knight OVA - 01 [1080p].mkv"]);
        Assert.Equal(2, Mapped(result)["Lantern Knight [Grp]/Specials/[Grp] Lantern Knight OVA - 02 [1080p].mkv"]);
    }

    /// <summary>SPEC-03: the only shared tokens are "the" and "of".</summary>
    [Fact(Skip = "PARSE-19")]
    public async Task TitleMatcher_StopwordsOnly_DoesNotMatch()
    {
        var pack = new SpecialShowBuilder("The Rise of the Lantern Knight", 900020)
            .Special(1, "Recap of the First Season", new DateTime(2019, 1, 1))
            .Special(2, "Behind the Scenes", new DateTime(2019, 2, 1))
            .SpecialFile("Knight [Grp]/Specials/[Grp] The Rise of the Lantern Knight OVA - 05 [1080p].mkv", null)
            .SpecialFile("Knight [Grp]/Specials/[Grp] The Rise of the Lantern Knight OVA - 06 [1080p].mkv", null)
            .SpecialFile("Knight [Grp]/Specials/[Grp] The Rise of the Lantern Knight OVA - 07 [1080p].mkv", null)
            .Build("stopword-title", "SPEC-03");
        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."));

        var result = await host.ResolveAsync(pack, useGemini: false);

        Assert.Empty(Mapped(result));
    }

    [Fact(Skip = "PARSE-19")]
    public void TitleMatcher_StopwordsAndShowNameOnly_ReturnsNull()
    {
        var specials = new List<TrackedEpisode>
        {
            SpecialShowBuilder.Episode(0, 1, "Recap of the First Season", new DateTime(2019, 1, 1)),
            SpecialShowBuilder.Episode(0, 2, "OVA 1: Hot Spring", new DateTime(2019, 6, 1))
        };
        const string fileName = "[Grp] The Rise of the Lantern Knight OVA - 05 [1080p].mkv";
        var parsed = TorrentCandidateParser.Parse(fileName, "Pack/Specials/" + fileName);

        Assert.Null(SpecialEpisodeTitleMatcher.TryMatch(parsed, specials, new HashSet<int>()));
    }

    [Fact(Skip = "PARSE-19")]
    public void TitleMatcher_TwoEntriesScoreTheSame_PicksNone()
    {
        var pack = SpecialCases.BrassShow().Build("brass", "titles only");
        const string fileName = "[Grp] Harbor Brass - Recap Part.mkv";
        var parsed = TorrentCandidateParser.Parse(fileName, "Pack/Specials/" + fileName);

        Assert.Null(SpecialEpisodeTitleMatcher.TryMatch(parsed, pack.Specials, new HashSet<int>()));
    }

    /// <summary>A descriptive special in a root Specials folder of a multi-season show: no block, only its title.</summary>
    [Fact(Skip = "PARSE-19")]
    public async Task TitleMatcher_OpaqueFileWithTmdbTitle_Matched()
    {
        var pack = new SpecialShowBuilder("Quiet Club", 900008)
            .Season(1, 13, new DateTime(2013, 4, 5))
            .Season(2, 13, new DateTime(2015, 4, 3))
            .Special(1, "A Festival Afterword", new DateTime(2013, 9, 19))
            .Special(2, "The Gift Exchange", new DateTime(2016, 10, 27))
            .Special(3, "The Last Prom", new DateTime(2023, 4, 27))
            .SpecialFile("Quiet Club/Specials/Quiet Club - The Gift Exchange.mkv", 2)
            .Build("opaque-title", "title only");
        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."));

        var result = await host.ResolveAsync(pack, useGemini: false);

        Assert.Equal(2, Mapped(result)["Quiet Club/Specials/Quiet Club - The Gift Exchange.mkv"]);
    }

    private static SpecialCase DirectPack(string specialFile) =>
        new SpecialShowBuilder("Quiet Club", 900008)
            .Season(1, 3, new DateTime(2013, 4, 5))
            .Special(1, "A Festival Afterword", new DateTime(2013, 9, 19))
            .Special(2, "The Gift Exchange", new DateTime(2013, 12, 24))
            .RegularFiles(1, "Quiet Club/Season 01", episode => $"Quiet Club S01E{episode:00}.mkv")
            .SpecialFile(specialFile, 2)
            .Build("direct", "S00Exx in the file name");

    private static SpecialCase OvaDashPack((string First, string Second) titles) =>
        new SpecialShowBuilder("Lantern Knight", 900021)
            .Special(1, titles.First, new DateTime(2015, 1, 1))
            .Special(2, titles.Second, new DateTime(2015, 2, 1))
            .Special(3, "Hot Spring Episode", new DateTime(2015, 6, 1))
            .Special(4, "Beach Episode", new DateTime(2015, 12, 1))
            .SpecialFile("Lantern Knight [Grp]/Specials/[Grp] Lantern Knight OVA - 01 [1080p].mkv", null)
            .SpecialFile("Lantern Knight [Grp]/Specials/[Grp] Lantern Knight OVA - 02 [1080p].mkv", null)
            .Build("ova-dash", "SPEC-02");

    private static Dictionary<string, int> Mapped(SpecialMappingResult result) =>
        result.Items
            .Where(item => item.Episode is not null)
            .ToDictionary(item => item.RelativePath, item => item.Episode!.EpisodeNumber, StringComparer.OrdinalIgnoreCase);
}
