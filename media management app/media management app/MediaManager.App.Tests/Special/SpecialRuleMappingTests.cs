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
    [Fact]
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
    [Theory]
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
    [Theory]
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
    [Fact]
    public async Task CompleteMiniSet_RulesOnly_AllThirtyTwoResolve()
    {
        var pack = SpecialCases.ByName("rooftop-complete-minis");
        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."));

        var result = await host.ResolveAsync(pack, useGemini: false);

        Assert.Equal(pack.ExpectedS00E.ToDictionary(pair => pair.Key, pair => pair.Value!.Value), Mapped(result));
    }

    /// <summary>
    /// Scenario I: a recap aired before season 1 is not in the pack, so season 1's block is not exact; once the
    /// recap is left out it is (Gemini linked S01S01 to the recap in every eval sample).
    /// </summary>
    [Fact]
    public async Task RecapBeforeSeasonOne_RulesOnly_RecapSkippedBothResolve()
    {
        var pack = SpecialCases.RecapBeforeSeasonOne();
        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."));

        var result = await host.ResolveAsync(pack, useGemini: true);

        Assert.Equal(
            new Dictionary<string, int> { ["Show/Season 1/Show.S01S01.mkv"] = 2, ["Show/Season 2/Show.S02S01.mkv"] = 3 },
            Mapped(result));
        Assert.Equal(SpecialMappingProposalReason.WindowWithoutAsides, Reason(result, "Show.S01S01.mkv"));
        Assert.Equal(SpecialMappingProposalReason.Window, Reason(result, "Show.S02S01.mkv"));
        Assert.Equal(0, host.Gemini.Calls);
    }

    /// <summary>A recap sits between mini 08 and mini 09: without it the window holds exactly the nine minis.</summary>
    [Theory]
    [InlineData("rail-untitled", "Night Rail - SP09.mkv")]
    [InlineData("rail-titled", "Night Rail - Mini Anime 09.mkv")]
    public async Task RecapInsideTheWindow_RulesOnly_SkippedAndEveryMiniResolves(string name, string lastMini)
    {
        var pack = SpecialCases.ByName(name);
        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."));

        var result = await host.ResolveAsync(pack, useGemini: true);

        Assert.Equal(pack.ExpectedS00E.ToDictionary(pair => pair.Key, pair => pair.Value!.Value), Mapped(result));
        Assert.Equal(SpecialMappingProposalReason.WindowWithoutAsides, Reason(result, lastMini));
        Assert.Equal(0, host.Gemini.Calls);
    }

    /// <summary>Leaving the preview out still leaves four entries for three files: nothing is placed.</summary>
    [Fact]
    public async Task AsideTitlesSkipped_CountStillDiffers_LeftOpen()
    {
        var pack = new SpecialShowBuilder("Copper Lantern", 900102)
            .Season(1, 12, new DateTime(2019, 4, 5))
            .Special(1, "Picture Drama 1", new DateTime(2019, 6, 26))
            .Special(2, "Picture Drama 2", new DateTime(2019, 7, 24))
            .Special(3, "Cast Talk Show", new DateTime(2019, 8, 1))
            .Special(4, "Picture Drama 3", new DateTime(2019, 8, 28))
            .Special(5, "Season 2 Preview", new DateTime(2019, 9, 25))
            .RegularFiles(1, "Copper Lantern/Season 01", episode => $"Copper Lantern - S01E{episode:00}.mkv")
            .SpecialFile("Copper Lantern/Season 01/Specials/Copper Lantern - S1S01.mkv", 1)
            .SpecialFile("Copper Lantern/Season 01/Specials/Copper Lantern - S1S02.mkv", 2)
            .SpecialFile("Copper Lantern/Season 01/Specials/Copper Lantern - S1S03.mkv", 4)
            .Build("aside-count-differs", "a talk show the rule cannot tell from the dramas");
        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."));

        var result = await host.ResolveAsync(pack, useGemini: false);

        Assert.Empty(Mapped(result));
    }

    /// <summary>
    /// Eval case root-ova-titled-tmdb: "OVA - 01..04" in a root folder, TMDB titles four entries "OVA:" with a recap
    /// and a drama CD between them. The k-th OVA is the k-th "OVA:" entry, not the k-th entry (Gemini Flash-Lite
    /// answered E1..E4 in every sample).
    /// </summary>
    [Fact]
    public async Task OvaFolder_TmdbNamesItsOvas_KthOvaTakesTheKthOvaEntry()
    {
        var show = OrchardShow();
        for (var index = 1; index <= 4; index++)
        {
            show.SpecialFile($"Glass Orchard/OVA/Glass Orchard - OVA - {index:00}.mkv", null);
        }

        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."));

        var result = await host.ResolveAsync(show.Build("root-ova", "OVA order"), useGemini: true);

        Assert.Equal(
            new Dictionary<string, int>
            {
                ["Glass Orchard/OVA/Glass Orchard - OVA - 01.mkv"] = 2,
                ["Glass Orchard/OVA/Glass Orchard - OVA - 02.mkv"] = 3,
                ["Glass Orchard/OVA/Glass Orchard - OVA - 03.mkv"] = 5,
                ["Glass Orchard/OVA/Glass Orchard - OVA - 04.mkv"] = 6
            },
            Mapped(result));
        Assert.All(result.Items, item => Assert.Equal(SpecialMappingProposalReason.OvaOrder, item.ProposalReason));
        Assert.Equal(0, host.Gemini.Calls);
    }

    /// <summary>A titled OVA the title rule placed on its own slot confirms the count; the others follow it.</summary>
    [Fact]
    public async Task OvaFolder_TitledOvaAgreesWithTheCount_OthersFollow()
    {
        var pack = OrchardShow()
            .SpecialFile("Glass Orchard/OVA/Glass Orchard - OVA - 01.mkv", 2)
            .SpecialFile("Glass Orchard/OVA/Glass Orchard - OVA - 02 - Winter Festival.mkv", 3)
            .SpecialFile("Glass Orchard/OVA/Glass Orchard - OVA - 03.mkv", 5)
            .SpecialFile("Glass Orchard/OVA/Glass Orchard - OVA - 04.mkv", 6)
            .Build("root-ova-titled-middle", "title confirms the count");
        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."));

        var result = await host.ResolveAsync(pack, useGemini: false);

        Assert.Equal(pack.ExpectedS00E.ToDictionary(pair => pair.Key, pair => pair.Value!.Value), Mapped(result));
        Assert.Equal(SpecialMappingProposalReason.Title, Reason(result, "Glass Orchard - OVA - 02 - Winter Festival.mkv"));
        Assert.Equal(SpecialMappingProposalReason.OvaOrder, Reason(result, "Glass Orchard - OVA - 03.mkv"));
    }

    /// <summary>OVA 02's title names the third OVA: the count and the title disagree, so the count places nothing.</summary>
    [Fact]
    public async Task OvaFolder_TitledOvaContradictsTheCount_GroupLeftOpen()
    {
        var pack = OrchardShow()
            .SpecialFile("Glass Orchard/OVA/Glass Orchard - OVA - 01.mkv", null)
            .SpecialFile("Glass Orchard/OVA/Glass Orchard - OVA - 02 - Hot Springs.mkv", 5)
            .SpecialFile("Glass Orchard/OVA/Glass Orchard - OVA - 03.mkv", null)
            .SpecialFile("Glass Orchard/OVA/Glass Orchard - OVA - 04.mkv", null)
            .Build("root-ova-title-veto", "title vetoes the count");
        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."));

        var result = await host.ResolveAsync(pack, useGemini: false);

        Assert.Equal(
            new Dictionary<string, int> { ["Glass Orchard/OVA/Glass Orchard - OVA - 02 - Hot Springs.mkv"] = 5 },
            Mapped(result));
    }

    /// <summary>Three OVA files for four "OVA:" entries: which one is missing is not known, so nothing is placed.</summary>
    [Fact]
    public async Task OvaFolder_MoreOvaEntriesThanFiles_LeftOpen()
    {
        var show = OrchardShow();
        for (var index = 1; index <= 3; index++)
        {
            show.SpecialFile($"Glass Orchard/OVA/Glass Orchard - OVA - {index:00}.mkv", null);
        }

        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."));

        var result = await host.ResolveAsync(show.Build("root-ova-missing", "count differs"), useGemini: false);

        Assert.Empty(Mapped(result));
    }

    /// <summary>
    /// A root OVA folder of a show with two seasons belongs to no window: the count is not tried across the whole
    /// Season 0 list (only a one-season show's root folder counts as that season).
    /// </summary>
    [Fact]
    public async Task OvaFolder_RootOfAMultiSeasonShow_NotCounted()
    {
        var pack = new SpecialShowBuilder("Glass Orchard", 900101)
            .Season(1, 12, new DateTime(2021, 1, 8))
            .Season(2, 12, new DateTime(2023, 1, 6))
            .Special(1, "OVA: Summer Trip", new DateTime(2021, 8, 25))
            .Special(2, "Recap Special", new DateTime(2022, 12, 20))
            .Special(3, "OVA: Hot Springs", new DateTime(2023, 8, 2))
            .RegularFiles(1, "Glass Orchard/Season 01", episode => $"Glass Orchard S01E{episode:00}.mkv")
            .RegularFiles(2, "Glass Orchard/Season 02", episode => $"Glass Orchard S02E{episode:00}.mkv")
            .SpecialFile("Glass Orchard/OVA/Glass Orchard - OVA - 01.mkv", 1)
            .SpecialFile("Glass Orchard/OVA/Glass Orchard - OVA - 02.mkv", 3)
            .Build("root-ova-two-seasons", "no window for a root folder");
        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."));

        var result = await host.ResolveAsync(pack, useGemini: false);

        Assert.DoesNotContain(result.Items, item => item.ProposalReason == SpecialMappingProposalReason.OvaOrder);
    }

    [Fact]
    public async Task Prepare_RulesOnlyMapping_RequiresReview()
    {
        var pack = SpecialCases.TwoSeasonBrassPack();
        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."), geminiEnabled: false);
        var service = SpecialPackPrepareHost.Create(pack, host.Orchestrator);

        var preview = await service.PrepareSeasonPackLinkAsync(SpecialPackPrepareHost.ShowId, 1, useGeminiForSpecials: false);

        Assert.True(preview.RequiresReview);
    }

    [Fact]
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

    /// <summary>
    /// Audit L2-01: "Part 1" and "Part 2" share every word, so the title alone does not confirm a S00E02 file
    /// that is really Part 1 (TVDB vs TMDB numbering). It must go to review.
    /// </summary>
    [Fact]
    public async Task Prepare_DirectS00EWhoseTitleNamesAnotherPart_RequiresReview()
    {
        var pack = NumberedPartsPack("Quiet Club/Specials/Quiet Club - S00E02 - Inside the Episode Part 1.mkv", 2);
        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."), geminiEnabled: false);
        var service = SpecialPackPrepareHost.Create(pack, host.Orchestrator);

        var preview = await service.PrepareSeasonPackLinkAsync(SpecialPackPrepareHost.ShowId, 1, useGeminiForSpecials: false);

        Assert.True(preview.RequiresReview);
    }

    [Fact]
    public async Task Prepare_DirectS00EWhoseTitleNamesTheSamePart_LinksWithoutReview()
    {
        var pack = NumberedPartsPack("Quiet Club/Specials/Quiet Club - S00E02 - Inside the Episode Part 2.mkv", 2);
        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."), geminiEnabled: false);
        var service = SpecialPackPrepareHost.Create(pack, host.Orchestrator);

        var preview = await service.PrepareSeasonPackLinkAsync(SpecialPackPrepareHost.ShowId, 1, useGeminiForSpecials: false);

        Assert.Equal(2, preview.Inventory.Files.Single(file => file.FileName.Contains("S00E02")).MatchedEpisodeNumber);
        Assert.False(preview.RequiresReview);
    }

    [Theory]
    [InlineData("Inside the Episode Part 1", 1, true)]
    [InlineData("Inside the Episode Part 1", 2, false)]
    [InlineData("Christmas Special 2018", 3, true)]
    [InlineData("Christmas Special 2018", 4, false)]
    [InlineData("Recap", 5, false)]
    public void TitleMatcher_Confirms_NeedsTheSameNumbersAndAUniqueBestTitle(string fileTitle, int episode, bool confirmed)
    {
        var specials = new List<TrackedEpisode>
        {
            SpecialShowBuilder.Episode(0, 1, "Inside the Episode: Part 1", new DateTime(2019, 1, 1)),
            SpecialShowBuilder.Episode(0, 2, "Inside the Episode: Part 2", new DateTime(2019, 1, 8)),
            SpecialShowBuilder.Episode(0, 3, "Christmas Special 2018", new DateTime(2018, 12, 25)),
            SpecialShowBuilder.Episode(0, 4, "Christmas Special 2019", new DateTime(2019, 12, 25)),
            SpecialShowBuilder.Episode(0, 5, "Recap", new DateTime(2020, 1, 1)),
            SpecialShowBuilder.Episode(0, 6, "Recap", new DateTime(2021, 1, 1))
        };

        var result = SpecialEpisodeTitleMatcher.Confirms(
            $"Quiet Club - S00E{episode:00} - {fileTitle}.mkv",
            "Quiet Club",
            specials.Single(item => item.EpisodeNumber == episode),
            specials);

        Assert.Equal(confirmed, result);
    }

    /// <summary>
    /// Audit R2-C-01: release tags ("DDP5.1", "H.264") must not supply the numbers a TMDB title needs, and a
    /// number in the file's own title must be in the TMDB title too.
    /// </summary>
    [Theory]
    [InlineData("Show.S00E01.Inside.the.Episode.Part.2.1080p.WEB-DL.DDP5.1.H.264.mkv", 1, false)]
    [InlineData("Show.S00E02.Inside.the.Episode.Part.2.Extended.1080p.WEB-DL.DDP5.1.H.264.mkv", 2, true)]
    [InlineData("Show.S00E01.Inside.the.Episode.Part.1.1080p.WEB-DL.AAC2.0.H.264.mkv", 1, true)]
    [InlineData("Show.S00E03.Behind.the.Music.Part.2.1080p.mkv", 3, false)]
    [InlineData("Show.S00E03.Behind.the.Music.1080p.WEB-DL.DDP5.1.mkv", 3, true)]
    public void TitleMatcher_Confirms_IgnoresReleaseTagNumbersAndComparesBothWays(string fileName, int episode, bool confirmed)
    {
        var specials = new List<TrackedEpisode>
        {
            SpecialShowBuilder.Episode(0, 1, "Inside the Episode: Part 1", new DateTime(2019, 1, 1)),
            SpecialShowBuilder.Episode(0, 2, "Inside the Episode: Part 2 (Extended)", new DateTime(2019, 1, 8)),
            SpecialShowBuilder.Episode(0, 3, "Behind the Music", new DateTime(2019, 2, 1))
        };

        var result = SpecialEpisodeTitleMatcher.Confirms(fileName, "Show", specials.Single(item => item.EpisodeNumber == episode), specials);

        Assert.Equal(confirmed, result);
    }

    /// <summary>
    /// Audit L2-02: <c>_S00E02_</c>, <c>S00E02v2</c> and <c>S0E2</c> still name S00E02, so the Direct rule places them
    /// (and the Gemini guard sees the number).
    /// </summary>
    [Theory]
    [InlineData("Quiet Club/Specials/Quiet_Club_S00E02_1080p.mkv")]
    [InlineData("Quiet Club/Specials/Quiet Club - S00E02v2 [1080p].mkv")]
    [InlineData("Quiet Club/Specials/Quiet Club S0E2.mkv")]
    public async Task DirectS00E_SeparatorsRevisionsAndShortSeason_MappedByTheNumberInTheName(string specialFile)
    {
        var pack = DirectPack(specialFile);
        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."));

        var result = await host.ResolveAsync(pack, useGemini: false);

        Assert.Equal(2, Mapped(result)[specialFile]);
    }

    /// <summary>
    /// Audit R2-C-02: a file whose name says a S00Exx number TMDB does not have can never take a Gemini answer (the
    /// guard drops any other number), so it is not sent: no request is spent on it, every time.
    /// </summary>
    [Fact]
    public async Task DirectS00ENotInTmdb_IsNotSentToGemini()
    {
        var pack = DirectPack("Quiet Club/Specials/Quiet Club - S00E09.mkv");
        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."));

        var result = await host.ResolveAsync(pack, useGemini: true);

        Assert.Equal(0, host.Gemini.Calls);
        Assert.DoesNotContain(result.Items, item => item.Episode is not null);
    }

    /// <summary>Audit R2-C-05: of two copies naming S00E02, the better quality is linked, not the first by path.</summary>
    [Fact]
    public async Task DirectS00E_TwoCopies_TheBetterQualityWins()
    {
        var pack = new SpecialShowBuilder("Quiet Club", 900008)
            .Season(1, 3, new DateTime(2013, 4, 5))
            .Special(1, "A Festival Afterword", new DateTime(2013, 9, 19))
            .Special(2, "The Gift Exchange", new DateTime(2013, 12, 24))
            .RegularFiles(1, "Quiet Club/Season 01", episode => $"Quiet Club S01E{episode:00}.mkv")
            .SpecialFile("Quiet Club/Specials/Quiet Club - S00E02 [720p].mkv", null)
            .SpecialFile("Quiet Club/Specials/Quiet Club - S00E02 [1080p].mkv", 2)
            .Build("direct-copies", "two copies of one S00Exx");
        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."));

        var result = await host.ResolveAsync(pack, useGemini: false);

        var mapped = Mapped(result);
        Assert.Equal(2, mapped["Quiet Club/Specials/Quiet Club - S00E02 [1080p].mkv"]);
        Assert.False(mapped.ContainsKey("Quiet Club/Specials/Quiet Club - S00E02 [720p].mkv"));
    }

    /// <summary>SPEC-02: "OVA - 01" is the first OVA, not TMDB S00E01 (here a recap).</summary>
    [Fact]
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
    [Fact]
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

    [Fact]
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

    [Fact]
    public void TitleMatcher_TwoEntriesScoreTheSame_PicksNone()
    {
        var pack = SpecialCases.BrassShow().Build("brass", "titles only");
        const string fileName = "[Grp] Harbor Brass - Encore Part.mkv";
        var parsed = TorrentCandidateParser.Parse(fileName, "Pack/Specials/" + fileName);

        Assert.Null(SpecialEpisodeTitleMatcher.TryMatch(parsed, pack.Specials, new HashSet<int>()));
    }

    /// <summary>A descriptive special in a root Specials folder of a multi-season show: no block, only its title.</summary>
    [Fact]
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

    /// <summary>One season; TMDB S00 has four "OVA:" entries with a recap (E1) and a drama CD (E4) between them.</summary>
    private static SpecialShowBuilder OrchardShow() =>
        new SpecialShowBuilder("Glass Orchard", 900101)
            .Season(1, 12, new DateTime(2021, 1, 8))
            .Special(1, "Recap Special", new DateTime(2021, 3, 20))
            .Special(2, "OVA: Summer Trip", new DateTime(2021, 8, 25))
            .Special(3, "OVA: Winter Festival", new DateTime(2021, 12, 22))
            .Special(4, "Picture Drama Collection", new DateTime(2022, 2, 23))
            .Special(5, "OVA: Hot Springs", new DateTime(2022, 4, 27))
            .Special(6, "OVA: The Last Day", new DateTime(2022, 6, 29))
            .RegularFiles(1, "Glass Orchard/Season 01", episode => $"Glass Orchard S01E{episode:00}.mkv");

    private static SpecialMappingProposalReason? Reason(SpecialMappingResult result, string fileName) =>
        result.Items.Single(item => item.RelativePath.EndsWith("/" + fileName, StringComparison.Ordinal)).ProposalReason;

    private static SpecialCase DirectPack(string specialFile) =>
        new SpecialShowBuilder("Quiet Club", 900008)
            .Season(1, 3, new DateTime(2013, 4, 5))
            .Special(1, "A Festival Afterword", new DateTime(2013, 9, 19))
            .Special(2, "The Gift Exchange", new DateTime(2013, 12, 24))
            .RegularFiles(1, "Quiet Club/Season 01", episode => $"Quiet Club S01E{episode:00}.mkv")
            .SpecialFile(specialFile, 2)
            .Build("direct", "S00Exx in the file name");

    private static SpecialCase NumberedPartsPack(string specialFile, int expected) =>
        new SpecialShowBuilder("Quiet Club", 900008)
            .Season(1, 3, new DateTime(2013, 4, 5))
            .Special(1, "Inside the Episode: Part 1", new DateTime(2013, 9, 19))
            .Special(2, "Inside the Episode: Part 2", new DateTime(2013, 9, 26))
            .RegularFiles(1, "Quiet Club/Season 01", episode => $"Quiet Club S01E{episode:00}.mkv")
            .SpecialFile(specialFile, expected)
            .Build("numbered-parts", "S00Exx with a numbered title");

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
