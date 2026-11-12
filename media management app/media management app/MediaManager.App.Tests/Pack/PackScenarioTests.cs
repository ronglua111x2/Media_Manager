using System.IO;
using System.Globalization;
using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.App.Tests.Pack;

// Parser audit 2026-11, report section 3: pack scenarios A-M run through
// PackTorrentInventoryAnalyzer.Analyze in Link mode, with specials resolved by the offline
// (rules-only) branch of SpecialMappingOrchestrator. Each test states the desired result; tests
// still broken carry Skip = the finding that fixes them.
public class PackScenarioTests
{
    [Fact]
    public void A_RootSeasonRangeFolderWithSpaces_KeepsEachFileInItsOwnSeason()
    {
        var scenario = Scenarios["A"];

        var inventory = PackScenario.Run(scenario);

        Assert.Equal(9, inventory.RegularEpisodeCount);
        foreach (var (season, episode) in SeasonEpisodeGrid(3, 3))
        {
            PackScenario.AssertRegular(inventory, $"Show Season 1-3 1080p/Show.S{season:00}E{episode:00}.1080p.mkv", season, episode);
        }
    }

    [Fact]
    public void B_RootSeasonRangeFolderWithSxx_KeepsEachFileInItsOwnSeason()
    {
        var scenario = Scenarios["B"];

        var inventory = PackScenario.Run(scenario);

        Assert.Equal(9, inventory.RegularEpisodeCount);
        foreach (var (season, episode) in SeasonEpisodeGrid(3, 3))
        {
            PackScenario.AssertRegular(inventory, $"Show.S01-S03.1080p/Show.S{season:00}E{episode:00}.1080p.mkv", season, episode);
        }
    }

    [Fact]
    public void C_ShowTitleContainsSpecial_EpisodesStayRegular()
    {
        var inventory = PackScenario.Run(Scenarios["C"]);

        Assert.Equal(8, inventory.RegularEpisodeCount);
        for (var episode = 1; episode <= 8; episode++)
        {
            PackScenario.AssertRegular(inventory, $"Special Ops Show/Season 1/Special.Ops.Show.S01E{episode:00}.1080p.mkv", 1, episode);
        }
    }

    [Fact]
    public void D_EpisodeTitleContainsMovie_EpisodeStaysRegular()
    {
        var inventory = PackScenario.Run(Scenarios["D"]);

        Assert.Equal(6, inventory.RegularEpisodeCount);
        Assert.Equal(0, inventory.MovieCount);
        PackScenario.AssertRegular(inventory, "Show/Season 1/Show.S01E04.Movie.Night.1080p.mkv", 1, 4);
    }

    [Fact]
    public void E_SampleNextToEpisode_SampleIsSkippedAndMainFileWins()
    {
        var inventory = PackScenario.Run(Scenarios["E"]);

        PackScenario.AssertRegular(inventory, "Show.S01.1080p/Show.S01E01.1080p.mkv", 1, 1);
        Assert.Equal(PackFileClassification.Skipped, PackScenario.Entry(inventory, "Show.S01.1080p/Sample/Show.S01E01.sample.mkv").Classification);
        Assert.Equal(3, inventory.RegularEpisodeCount);
    }

    [Fact]
    public void F_MultiEpisodeFile_LinksBothEpisodes()
    {
        var inventory = PackScenario.Run(Scenarios["F"]);

        PackScenario.AssertRegular(inventory, "Show/Season 1/Show.S01E01E02.1080p.mkv", 1, 1);
        Assert.Equal(2, PackScenario.Entry(inventory, "Show/Season 1/Show.S01E01E02.1080p.mkv").MatchedLastEpisodeNumber);
        PackScenario.AssertRegular(inventory, "Show/Season 1/Show.S01E04.1080p.mkv", 1, 4);
        PackScenario.AssertRegular(inventory, "Show/Season 1/Show.S01E05.1080p.mkv", 1, 5);
    }

    [Fact]
    public void F_RevisionGluedToEpisodeNumber_WinsOverOriginal()
    {
        var inventory = PackScenario.Run(Scenarios["F"]);

        PackScenario.AssertRegular(inventory, "Show/Season 1/Show.S01E03v2.1080p.mkv", 1, 3);
        Assert.Equal(PackFileClassification.Skipped, PackScenario.Entry(inventory, "Show/Season 1/Show.S01E03.1080p.mkv").Classification);
    }

    [Fact]
    public void G_AbsoluteNumberedSecondSeason_ConvertsToSeasonEpisodes()
    {
        var inventory = PackScenario.Run(Scenarios["G"]);

        Assert.Equal(12, inventory.RegularEpisodeCount);
        for (var absolute = 13; absolute <= 24; absolute++)
        {
            PackScenario.AssertRegular(inventory, $"[Group] Show S2/[Group] Show - {absolute:00} [1080p].mkv", 2, absolute - 12);
        }
    }

    [Fact]
    public void H_AbsoluteNumberedBatch_LinksAllTwelveEpisodes()
    {
        var inventory = PackScenario.Run(Scenarios["H"]);

        Assert.Equal(12, inventory.RegularEpisodeCount);
        for (var episode = 1; episode <= 12; episode++)
        {
            PackScenario.AssertRegular(
                inventory,
                $"[SubsPlease] Show (01-12) (1080p) [Batch]/[SubsPlease] Show - {episode:00} (1080p) [ABCD12{episode:00}].mkv",
                1,
                episode);
        }
    }

    // Same shape as the real BD batch that linked only 9/12 (audit section 4), with a generic name.
    [Fact]
    public void H_AbsoluteNumberedBdBatchWithoutCrc_LinksAllTwelveEpisodes()
    {
        var inventory = PackScenario.Run(Scenarios["H-bd"]);

        Assert.Equal(12, inventory.RegularEpisodeCount);
        for (var episode = 1; episode <= 12; episode++)
        {
            PackScenario.AssertRegular(
                inventory,
                $"Show Name? (BD 1080p HEVC Opus) [Grp]/Show Name? - {episode:00} (BD 1080p HEVC Opus) [Grp].mkv",
                1,
                episode);
        }
    }

    [Fact]
    public void I_SeasonSpecialFiles_AreNotGuessedOntoUnrelatedSpecials()
    {
        var inventory = PackScenario.Run(Scenarios["I"]);

        PackScenario.AssertRegular(inventory, "Show/Season 1/Show.S01E01.mkv", 1, 1);
        PackScenario.AssertRegular(inventory, "Show/Season 2/Show.S02E02.mkv", 2, 2);
        var firstSpecial = PackScenario.Entry(inventory, "Show/Season 1/Show.S01S01.mkv");
        var secondSpecial = PackScenario.Entry(inventory, "Show/Season 2/Show.S02S01.mkv");
        Assert.False(firstSpecial.Classification == PackFileClassification.MatchedSpecial && firstSpecial.MatchedEpisodeNumber == 1);
        Assert.False(secondSpecial.Classification == PackFileClassification.MatchedSpecial && secondSpecial.MatchedEpisodeNumber == 2);
    }

    [Fact]
    public void J_TwoQualitiesOfOneEpisode_PicksHigherQuality()
    {
        var inventory = PackScenario.Run(Scenarios["J"]);

        PackScenario.AssertRegular(inventory, "Show/Season 1/1080p/Show.S01E01.1080p.mkv", 1, 1);
        Assert.Equal(PackFileClassification.Skipped, PackScenario.Entry(inventory, "Show/Season 1/Show.S01E01.720p.mkv").Classification);
        PackScenario.AssertRegular(inventory, "Show/Season 1/Show.S01E02.720p.mkv", 1, 2);
    }

    // Same as J but the lower quality sorts first by path, so only an explicit quality rule picks 1080p.
    [Fact]
    public void J_TwoQualitiesOfOneEpisode_PicksHigherQualityRegardlessOfPathOrder()
    {
        var inventory = PackScenario.Run(Scenarios["J-reversed"]);

        PackScenario.AssertRegular(inventory, "Show/Season 1/Show.S01E01.1080p.mkv", 1, 1);
        Assert.Equal(PackFileClassification.Skipped, PackScenario.Entry(inventory, "Show/Season 1/720p/Show.S01E01.720p.mkv").Classification);
    }

    // Two copies with the same revision and quality are a sign of a wrong mapping, not a better
    // copy: neither is linked and the reason says why (PR-7). Size alone does not pick a winner.
    [Fact]
    public void SameRevisionAndQualityCopies_AreBothSkippedWithReason()
    {
        var inventory = PackScenario.Run(Scenarios["sizes"]);

        foreach (var path in new[] { "Show/Season 1/a/Show.S01E01.1080p.mkv", "Show/Season 1/b/Show.S01E01.1080p.mkv" })
        {
            var entry = PackScenario.Entry(inventory, path);
            Assert.Equal(PackFileClassification.Skipped, entry.Classification);
            Assert.Contains("same revision and quality", entry.MatchReason);
        }
    }

    [Fact]
    public void AbsoluteBatchAcrossTwoSeasons_SplitsBySeasonEpisodeCounts()
    {
        var inventory = PackScenario.Run(Scenarios["absolute-two-seasons"]);

        Assert.Equal(24, inventory.RegularEpisodeCount);
        PackScenario.AssertRegular(inventory, "[Grp] Show (01-24)/[Grp] Show - 12 [1080p].mkv", 1, 12);
        PackScenario.AssertRegular(inventory, "[Grp] Show (01-24)/[Grp] Show - 13 [1080p].mkv", 2, 1);
        PackScenario.AssertRegular(inventory, "[Grp] Show (01-24)/[Grp] Show - 24 [1080p].mkv", 2, 12);
    }

    // Audit L1-H2: TMDB counts a recap as S01E13, the release starts season 2 at "- 13". Count-based
    // placement would link "- 13" as S01E13 and "- 14".."- 24" as S02E01..E11 (one off). None is linked.
    [Fact]
    public void AbsoluteSeasonFolder_TmdbCountsOneMore_NothingLinkedOneOff()
    {
        var inventory = PackScenario.Run(Scenarios["absolute-folder-tmdb-longer"]);

        for (var absolute = 13; absolute <= 24; absolute++)
        {
            var entry = PackScenario.Entry(inventory, $"Show/Season 2/[Group] Show - {absolute:00} [1080p].mkv");
            Assert.Equal(PackFileClassification.Skipped, entry.Classification);
        }

        Assert.Contains("do not line up", PackScenario.Entry(inventory, "Show/Season 2/[Group] Show - 14 [1080p].mkv").MatchReason);
    }

    // Reverse: TMDB season 1 has 12, the release's season 1 has a 13th file and season 2 starts at "- 14".
    [Fact]
    public void AbsoluteSeasonFolder_ReleaseCountsOneMore_NothingLinkedOneOff()
    {
        var inventory = PackScenario.Run(Scenarios["absolute-folder-release-longer"]);

        for (var absolute = 14; absolute <= 25; absolute++)
        {
            var entry = PackScenario.Entry(inventory, $"Show/Season 2/[Group] Show - {absolute:00} [1080p].mkv");
            Assert.Equal(PackFileClassification.Skipped, entry.Classification);
        }
    }

    [Fact]
    public void AbsoluteNumberBeyondShow_IsSkippedWithReason()
    {
        var inventory = PackScenario.Run(Scenarios["absolute-beyond"]);

        var entry = PackScenario.Entry(inventory, "[Grp] Show/[Grp] Show - 30 [1080p].mkv");
        Assert.Equal(PackFileClassification.Skipped, entry.Classification);
        Assert.Contains("beyond", entry.MatchReason);
    }

    // Unpadded "- N" names with different CRC tags: no fixed-width pattern, so the order comes from
    // the file names and must be natural ("- 2" before "- 10").
    [Fact]
    public void Specials_UnpaddedDashNumbersWithCrc_AreNeverMappedOutOfOrder()
    {
        var inventory = PackScenario.Run(Scenarios["SPEC-01-unpadded"]);

        for (var number = 1; number <= 12; number++)
        {
            var entry = PackScenario.Entry(inventory, $"Show [Grp]/Specials/[Grp] Show - {number} [{number:X8}].mkv");
            Assert.Equal(PackFileClassification.MatchedSpecial, entry.Classification);
            Assert.Equal(number, entry.MatchedEpisodeNumber);
        }
    }

    [Fact]
    public void K_SeasonPartTwoFolder_IsNotLinkedOverPartOne()
    {
        var inventory = PackScenario.Run(Scenarios["K"]);

        for (var episode = 1; episode <= 3; episode++)
        {
            PackScenario.AssertRegular(inventory, $"Show/Season 1/Show.S01E{episode:00}.mkv", 1, episode);
            var partTwo = PackScenario.Entry(inventory, $"Show/Season 1 Part 2/Show.S01E{episode:00}.mkv");
            Assert.Equal(PackFileClassification.Skipped, partTwo.Classification);
            Assert.Contains("Part", partTwo.MatchReason, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void L_MultiEpisodeFileInMultiSeasonPack_StaysInItsSeason()
    {
        var inventory = PackScenario.Run(Scenarios["L"]);

        PackScenario.AssertRegular(inventory, "Show.Complete/Show.S03E01E02.mkv", 3, 1);
        Assert.Equal(2, PackScenario.Entry(inventory, "Show.Complete/Show.S03E01E02.mkv").MatchedLastEpisodeNumber);
        PackScenario.AssertRegular(inventory, "Show.Complete/Show.S03E03.mkv", 3, 3);
        PackScenario.AssertRegular(inventory, "Show.Complete/Show.S02E02.mkv", 2, 2);
    }

    [Fact]
    public void M_EpisodeTitleContainsSpecial_EpisodeStaysRegular()
    {
        var inventory = PackScenario.Run(Scenarios["M"]);

        Assert.Equal(8, inventory.RegularEpisodeCount);
        PackScenario.AssertRegular(inventory, "Show/Season 2/Show.S02E07.A.Very.Special.Episode.mkv", 2, 7);
    }

    // Appendix A, SPEC-01: twelve "- NN" specials must not be ordered by their last digit.
    [Fact]
    public void Specials_DashNumberedTwelve_AreNeverMappedOutOfOrder()
    {
        var inventory = PackScenario.Run(Scenarios["SPEC-01"]);

        for (var number = 1; number <= 12; number++)
        {
            var entry = PackScenario.Entry(inventory, $"Show [Grp]/Specials/[Grp] Show - {number:00} [1080p].mkv");
            if (entry.Classification == PackFileClassification.MatchedSpecial)
            {
                Assert.Equal(number, entry.MatchedEpisodeNumber);
            }
        }
    }

    // Report section 10.4 point 5: a release of shorts named like regular episodes, whose titles
    // match specials and not the season's episodes, must not be linked as episodes 1-6.
    [Fact]
    public void ShortsNamedLikeEpisodes_WithSpecialTitles_AreNotLinkedAsRegularEpisodes()
    {
        var inventory = PackScenario.Run(Scenarios["shorts-titled"]);

        Assert.Equal(0, inventory.RegularEpisodeCount);
        Assert.Contains(inventory.Warnings, warning => warning.Contains("special", StringComparison.OrdinalIgnoreCase));
    }

    // The range stops where TMDB has no episode: S01E05-E06 in a 5-episode season links as E05 only.
    [Fact]
    public void MultiEpisodeFile_RangeBeyondTmdb_IsTrimmed()
    {
        var inventory = PackScenario.Run(Scenarios["multi-trim"]);

        PackScenario.AssertRegular(inventory, "Show/Season 1/Show.S01E05-E06.mkv", 1, 5);
        Assert.Null(PackScenario.Entry(inventory, "Show/Season 1/Show.S01E05-E06.mkv").MatchedLastEpisodeNumber);
    }

    [Fact]
    public void ShortsMixedIntoAPack_FarSmallerThanOtherEpisodes_AreHeldForReview()
    {
        var inventory = PackScenario.Run(Scenarios["shorts-by-size"]);

        Assert.Equal(6, inventory.RegularEpisodeCount);
        foreach (var number in new[] { 7, 8 })
        {
            var entry = PackScenario.Entry(inventory, $"[Grp] Show/[Grp] Show - {number:00} [1080p].mkv");
            Assert.Equal(PackFileClassification.Skipped, entry.Classification);
            Assert.Contains("smaller", entry.MatchReason);
        }
    }

    [Fact]
    public void SeasonFolderDisagreesWithFileName_FileNameWins()
    {
        var inventory = PackScenario.Run(Scenarios["folder-vs-name"]);

        PackScenario.AssertRegular(inventory, "Show/Season 2/Show.S01E03.mkv", 1, 3);
        PackScenario.AssertRegular(inventory, "Show/Season 2/Show.S02E01.mkv", 2, 1);
    }

    [Fact]
    public void S00EpisodeFileWithoutSpecialsFolder_IsNotLinkedAsRegularEpisode()
    {
        var inventory = PackScenario.Run(Scenarios["s00-in-root"]);

        PackScenario.AssertRegular(inventory, "Show.S01/Show.S01E02.mkv", 1, 2);
        Assert.NotEqual(PackFileClassification.RegularEpisode, PackScenario.Entry(inventory, "Show.S01/Show.S00E02.mkv").Classification);
    }

    // PARSE-15: daily releases resolve by TMDB air date; a day with two episodes is not guessed.
    [Fact]
    public void DailyReleases_ResolveByAirDate()
    {
        var inventory = PackScenario.Run(Scenarios["daily"]);

        PackScenario.AssertRegular(inventory, "Daily Show Week [Grp]/Daily.Show.2026.11.10.Guest.A.1080p.WEB.mkv", 5, 2);
        PackScenario.AssertRegular(inventory, "Daily Show Week [Grp]/Daily.Show.2026.11.11.Guest.B.1080p.WEB.mkv", 5, 3);
        var sameDay = PackScenario.Entry(inventory, "Daily Show Week [Grp]/Daily.Show.2026.11.12.Guest.C.1080p.WEB.mkv");
        Assert.Equal(PackFileClassification.Skipped, sameDay.Classification);
        Assert.Contains("aired that day", sameDay.MatchReason);
    }

    // Report section 6: Sonarr reads a year as season 20 ("Feature.2016" -> S20E16).
    [Fact]
    public void YearReadAsSeason_BeyondShowSeasons_IsNotLinked()
    {
        var inventory = PackScenario.Run(Scenarios["year-as-season"]);

        PackScenario.AssertRegular(inventory, "Show.Complete/Show.S01E02.mkv", 1, 2);
        Assert.NotEqual(PackFileClassification.RegularEpisode, PackScenario.Entry(inventory, "Show.Complete/Your.Feature.2016.2160p.BluRay.x265.mkv").Classification);
    }

    // Same, for a show that does have a season 20: the year must not become S20E16.
    [Fact]
    public void YearReadAsSeason_ShowWithTwentySeasons_IsNotLinked()
    {
        var inventory = PackScenario.Run(Scenarios["year-as-season-20"]);

        PackScenario.AssertRegular(inventory, "Show.S20/Show.S20E01.mkv", 20, 1);
        var feature = PackScenario.Entry(inventory, "Show.S20/Your.Feature.2016.2160p.BluRay.x265.mkv");
        Assert.NotEqual(PackFileClassification.RegularEpisode, feature.Classification);
    }

    // PARSE-15 (Inspect): a file with no episode number is not counted as an episode.
    [Fact]
    public void Inspect_FileWithoutEpisodeNumber_IsNotCountedAsEpisode()
    {
        var files = new List<(string, string)>
        {
            ("Show/Season 1/Show.S01E01.mkv", "Show.S01E01.mkv"),
            ("Show/Season 1/Show.S01E02.mkv", "Show.S01E02.mkv"),
            ("Show/Season 1/Behind the scenes footage.mkv", "Behind the scenes footage.mkv"),
        };
        var episodes = Enumerable.Range(1, 3).Select(e => new TrackedEpisode { SeasonNumber = 1, EpisodeNumber = e, Title = $"Episode {e}" }).ToList();

        var inventory = PackTorrentInventoryAnalyzer.Analyze(files, episodes, mode: PackAnalyzeMode.Inspect, ownerSeasonNumber: 1);

        Assert.Equal(2, inventory.RegularEpisodeCount);
        Assert.Equal(PackFileClassification.Skipped, PackScenario.Entry(inventory, "Show/Season 1/Behind the scenes footage.mkv").Classification);
        Assert.Equal(2, inventory.EpisodeCountsBySeason[1]);
    }

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public void AnyScenario_ReversedFileOrder_GivesSameResult(string name)
    {
        var scenario = Scenarios[name];

        var forward = PackScenario.Run(scenario);
        var reversed = PackScenario.Run(scenario with { Files = scenario.Files.Reverse().ToArray() });

        foreach (var file in scenario.Files)
        {
            var a = PackScenario.Entry(forward, file);
            var b = PackScenario.Entry(reversed, file);
            Assert.Equal((a.Classification, a.MatchedSeasonNumber, a.MatchedEpisodeNumber), (b.Classification, b.MatchedSeasonNumber, b.MatchedEpisodeNumber));
        }
    }

    public static IEnumerable<object[]> ScenarioNames() => Scenarios.Keys.Select(name => new object[] { name });

    private static IEnumerable<(int Season, int Episode)> SeasonEpisodeGrid(int seasons, int episodes) =>
        Enumerable.Range(1, seasons).SelectMany(season => Enumerable.Range(1, episodes).Select(episode => (season, episode)));

    private static string[] Grid(string template, int seasons, int episodes) =>
        SeasonEpisodeGrid(seasons, episodes)
            .Select(item => string.Format(CultureInfo.InvariantCulture, template, item.Season, item.Episode))
            .ToArray();

    private static readonly IReadOnlyDictionary<string, PackScenarioDefinition> Scenarios =
        new Dictionary<string, PackScenarioDefinition>
        {
            ["A"] = new(
                Owner: 1, Covered: [1, 2, 3], EpisodesPerSeason: new() { [1] = 3, [2] = 3, [3] = 3 },
                Files: Grid("Show Season 1-3 1080p/Show.S{0:00}E{1:00}.1080p.mkv", 3, 3)),
            ["B"] = new(
                Owner: 1, Covered: [1, 2, 3], EpisodesPerSeason: new() { [1] = 3, [2] = 3, [3] = 3 },
                Files: Grid("Show.S01-S03.1080p/Show.S{0:00}E{1:00}.1080p.mkv", 3, 3)),
            ["C"] = new(
                Owner: 1, Covered: [1], EpisodesPerSeason: new() { [1] = 8 },
                Files: Enumerable.Range(1, 8).Select(e => $"Special Ops Show/Season 1/Special.Ops.Show.S01E{e:00}.1080p.mkv").ToArray(),
                Specials: [(1, "2023-01-01", "Behind the Scenes")]),
            ["D"] = new(
                Owner: 1, Covered: [1], EpisodesPerSeason: new() { [1] = 6 },
                Files: Enumerable.Range(1, 6).Select(e => e == 4
                    ? "Show/Season 1/Show.S01E04.Movie.Night.1080p.mkv"
                    : $"Show/Season 1/Show.S01E{e:00}.1080p.mkv").ToArray()),
            ["E"] = new(
                Owner: 1, Covered: [1], EpisodesPerSeason: new() { [1] = 3 },
                Files:
                [
                    "Show.S01.1080p/Sample/Show.S01E01.sample.mkv",
                    "Show.S01.1080p/Show.S01E01.1080p.mkv",
                    "Show.S01.1080p/Show.S01E02.1080p.mkv",
                    "Show.S01.1080p/Show.S01E03.1080p.mkv",
                ]),
            ["F"] = new(
                Owner: 1, Covered: [1], EpisodesPerSeason: new() { [1] = 5 },
                Files:
                [
                    "Show/Season 1/Show.S01E01E02.1080p.mkv",
                    "Show/Season 1/Show.S01E03.1080p.mkv",
                    "Show/Season 1/Show.S01E03v2.1080p.mkv",
                    "Show/Season 1/Show.S01E04.1080p.mkv",
                    "Show/Season 1/Show.S01E05.1080p.mkv",
                ]),
            ["G"] = new(
                Owner: 2, Covered: [2], EpisodesPerSeason: new() { [1] = 12, [2] = 12 },
                Files: Enumerable.Range(13, 12).Select(e => $"[Group] Show S2/[Group] Show - {e:00} [1080p].mkv").ToArray()),
            ["H"] = new(
                Owner: 1, Covered: [1], EpisodesPerSeason: new() { [1] = 12 },
                Files: Enumerable.Range(1, 12).Select(e => $"[SubsPlease] Show (01-12) (1080p) [Batch]/[SubsPlease] Show - {e:00} (1080p) [ABCD12{e:00}].mkv").ToArray()),
            ["H-bd"] = new(
                Owner: 1, Covered: [1], EpisodesPerSeason: new() { [1] = 12 },
                Files: Enumerable.Range(1, 12).Select(e => $"Show Name? (BD 1080p HEVC Opus) [Grp]/Show Name? - {e:00} (BD 1080p HEVC Opus) [Grp].mkv").ToArray()),
            ["I"] = new(
                Owner: 1, Covered: [1, 2], EpisodesPerSeason: new() { [1] = 2, [2] = 2 },
                Files:
                [
                    "Show/Season 1/Show.S01E01.mkv", "Show/Season 1/Show.S01E02.mkv", "Show/Season 1/Show.S01S01.mkv",
                    "Show/Season 2/Show.S02E01.mkv", "Show/Season 2/Show.S02E02.mkv", "Show/Season 2/Show.S02S01.mkv",
                ],
                Specials:
                [
                    (1, "2010-05-01", "Recap Special"),
                    (2, "2021-03-01", "Season One Special"),
                    (3, "2022-03-01", "Season Two Special"),
                    (4, "2024-01-01", "Promo"),
                ]),
            ["J"] = new(
                Owner: 1, Covered: [1], EpisodesPerSeason: new() { [1] = 2 },
                Files:
                [
                    "Show/Season 1/Show.S01E01.720p.mkv",
                    "Show/Season 1/1080p/Show.S01E01.1080p.mkv",
                    "Show/Season 1/Show.S01E02.720p.mkv",
                ]),
            ["J-reversed"] = new(
                Owner: 1, Covered: [1], EpisodesPerSeason: new() { [1] = 2 },
                Files:
                [
                    "Show/Season 1/720p/Show.S01E01.720p.mkv",
                    "Show/Season 1/Show.S01E01.1080p.mkv",
                    "Show/Season 1/Show.S01E02.1080p.mkv",
                ]),
            ["sizes"] = new(
                Owner: 1, Covered: [1], EpisodesPerSeason: new() { [1] = 1 },
                Files: ["Show/Season 1/a/Show.S01E01.1080p.mkv", "Show/Season 1/b/Show.S01E01.1080p.mkv"],
                Sizes: new() { ["Show/Season 1/a/Show.S01E01.1080p.mkv"] = 700_000_000, ["Show/Season 1/b/Show.S01E01.1080p.mkv"] = 1_400_000_000 }),
            ["absolute-two-seasons"] = new(
                Owner: 1, Covered: [1, 2], EpisodesPerSeason: new() { [1] = 12, [2] = 12 },
                Files: Enumerable.Range(1, 24).Select(e => $"[Grp] Show (01-24)/[Grp] Show - {e:00} [1080p].mkv").ToArray()),
            ["absolute-folder-tmdb-longer"] = new(
                Owner: 2, Covered: [2], EpisodesPerSeason: new() { [1] = 13, [2] = 12 },
                Files: Enumerable.Range(13, 12).Select(e => $"Show/Season 2/[Group] Show - {e:00} [1080p].mkv").ToArray()),
            ["absolute-folder-release-longer"] = new(
                Owner: 2, Covered: [2], EpisodesPerSeason: new() { [1] = 12, [2] = 12 },
                Files: Enumerable.Range(14, 12).Select(e => $"Show/Season 2/[Group] Show - {e:00} [1080p].mkv").ToArray()),
            ["absolute-beyond"] = new(
                Owner: 1, Covered: [1], EpisodesPerSeason: new() { [1] = 12 },
                Files: Enumerable.Range(1, 12).Select(e => $"[Grp] Show/[Grp] Show - {e:00} [1080p].mkv")
                    .Append("[Grp] Show/[Grp] Show - 30 [1080p].mkv")
                    .ToArray()),
            ["SPEC-01-unpadded"] = new(
                Owner: 1, Covered: [1], EpisodesPerSeason: new() { [1] = 12 },
                Files: Enumerable.Range(1, 12).Select(e => $"Show [Grp]/Specials/[Grp] Show - {e} [{e:X8}].mkv").ToArray(),
                Specials: Enumerable.Range(1, 12)
                    .Select(e => (e, new DateTime(2020, 1, 1).AddDays(7 * e).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), $"Special {e}"))
                    .ToArray(),
                // Season 1 air dates let the special season-window mapper (track C) place the specials;
                // the whole-pack Ordinal rule this scenario first relied on no longer exists.
                AirDates: Enumerable.Range(1, 12).ToDictionary(
                    e => (1, e),
                    e => new DateTime(2019, 10, 1).AddDays(7 * (e - 1)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))),
            ["shorts-by-size"] = new(
                Owner: 1, Covered: [1], EpisodesPerSeason: new() { [1] = 8 },
                Files: Enumerable.Range(1, 8).Select(e => $"[Grp] Show/[Grp] Show - {e:00} [1080p].mkv").ToArray(),
                Sizes: Enumerable.Range(1, 8).ToDictionary(
                    e => $"[Grp] Show/[Grp] Show - {e:00} [1080p].mkv",
                    e => e <= 6 ? 1_300_000_000L : 60_000_000L)),
            ["folder-vs-name"] = new(
                Owner: 1, Covered: [1, 2], EpisodesPerSeason: new() { [1] = 3, [2] = 3 },
                Files: ["Show/Season 2/Show.S01E03.mkv", "Show/Season 2/Show.S02E01.mkv"]),
            ["s00-in-root"] = new(
                Owner: 1, Covered: [1], EpisodesPerSeason: new() { [1] = 3 },
                Files: ["Show.S01/Show.S01E01.mkv", "Show.S01/Show.S01E02.mkv", "Show.S01/Show.S00E02.mkv"],
                Specials: [(2, "2021-01-01", "Holiday Special")]),
            ["multi-trim"] = new(
                Owner: 1, Covered: [1], EpisodesPerSeason: new() { [1] = 5 },
                Files: ["Show/Season 1/Show.S01E01-E02.mkv", "Show/Season 1/Show.S01E03.mkv", "Show/Season 1/Show.S01E04.mkv", "Show/Season 1/Show.S01E05-E06.mkv"]),
            ["daily"] = new(
                Owner: 5, Covered: [5], EpisodesPerSeason: new() { [5] = 5 },
                Files:
                [
                    "Daily Show Week [Grp]/Daily.Show.2026.11.10.Guest.A.1080p.WEB.mkv",
                    "Daily Show Week [Grp]/Daily.Show.2026.11.11.Guest.B.1080p.WEB.mkv",
                    "Daily Show Week [Grp]/Daily.Show.2026.11.12.Guest.C.1080p.WEB.mkv",
                ],
                AirDates: new() { [(5, 1)] = "2026-11-09", [(5, 2)] = "2026-11-10", [(5, 3)] = "2026-11-11", [(5, 4)] = "2026-11-12", [(5, 5)] = "2026-11-12" }),
            ["year-as-season"] = new(
                Owner: 1, Covered: [1, 2], EpisodesPerSeason: new() { [1] = 3, [2] = 3 },
                Files: ["Show.Complete/Show.S01E01.mkv", "Show.Complete/Show.S01E02.mkv", "Show.Complete/Your.Feature.2016.2160p.BluRay.x265.mkv"]),
            ["year-as-season-20"] = new(
                Owner: 20, Covered: [20], EpisodesPerSeason: new() { [19] = 20, [20] = 20, [21] = 10 },
                Files: ["Show.S20/Show.S20E01.mkv", "Show.S20/Show.S20E02.mkv", "Show.S20/Your.Feature.2016.2160p.BluRay.x265.mkv"]),
            ["K"] = new(
                Owner: 1, Covered: [1], EpisodesPerSeason: new() { [1] = 6 },
                Files: Enumerable.Range(1, 3).Select(e => $"Show/Season 1/Show.S01E{e:00}.mkv")
                    .Concat(Enumerable.Range(1, 3).Select(e => $"Show/Season 1 Part 2/Show.S01E{e:00}.mkv"))
                    .ToArray()),
            ["L"] = new(
                Owner: 1, Covered: [1, 2, 3], EpisodesPerSeason: new() { [1] = 3, [2] = 3, [3] = 3 },
                Files:
                [
                    "Show.Complete/Show.S01E01.mkv", "Show.Complete/Show.S01E02.mkv", "Show.Complete/Show.S01E03.mkv",
                    "Show.Complete/Show.S02E01.mkv", "Show.Complete/Show.S02E02.mkv", "Show.Complete/Show.S02E03.mkv",
                    "Show.Complete/Show.S03E01E02.mkv", "Show.Complete/Show.S03E03.mkv",
                ]),
            ["M"] = new(
                Owner: 2, Covered: [2], EpisodesPerSeason: new() { [2] = 8 },
                Files: Enumerable.Range(1, 8).Select(e => e == 7
                    ? "Show/Season 2/Show.S02E07.A.Very.Special.Episode.mkv"
                    : $"Show/Season 2/Show.S02E{e:00}.mkv").ToArray(),
                Specials: [(1, "2020-01-01", "Special One"), (2, "2021-01-01", "Special Two")]),
            ["SPEC-01"] = new(
                Owner: 1, Covered: [1], EpisodesPerSeason: new() { [1] = 12 },
                Files: Enumerable.Range(1, 12).Select(e => $"Show [Grp]/Specials/[Grp] Show - {e:00} [1080p].mkv").ToArray(),
                Specials: Enumerable.Range(1, 12)
                    .Select(e => (e, new DateTime(2020, 1, 1).AddDays(7 * e).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), $"Special {e}"))
                    .ToArray()),
            ["shorts-titled"] = new(
                Owner: 1, Covered: [1], EpisodesPerSeason: new() { [1] = 12 },
                Files: Enumerable.Range(1, 6).Select(e => $"Show Shorts [Grp]/[Grp] Show - {e:00} - Little Story {(char)('A' + e - 1)} [1080p].mkv").ToArray(),
                Specials: Enumerable.Range(1, 6)
                    .Select(e => (e, "2025-04-01", $"Little Story {(char)('A' + e - 1)}"))
                    .ToArray()),
        };
}

internal sealed record PackScenarioDefinition(
    int Owner,
    int[] Covered,
    Dictionary<int, int> EpisodesPerSeason,
    string[] Files,
    (int Episode, string AirDate, string Title)[]? Specials = null,
    Dictionary<string, long>? Sizes = null,
    Dictionary<(int Season, int Episode), string>? AirDates = null);

internal static class PackScenario
{
    public static PackTorrentInventory Run(PackScenarioDefinition scenario)
    {
        var episodes = BuildEpisodes(scenario);
        var seasons = scenario.EpisodesPerSeason
            .Select(pair => new TrackedSeason { SeasonNumber = pair.Key, EpisodeCount = pair.Value })
            .ToList();
        var files = scenario.Files.Select(path => (path, Path.GetFileName(path))).ToList();
        var specials = episodes.Where(episode => episode.SeasonNumber == 0).ToList();

        return PackTorrentInventoryAnalyzer.Analyze(
            files,
            episodes,
            seasons,
            scenario.Covered.ToHashSet(),
            PackAnalyzeMode.Link,
            ResolveSpecialsOffline(files, specials, episodes.Where(episode => episode.SeasonNumber > 0).ToList()),
            ownerSeasonNumber: scenario.Owner,
            fileSizes: scenario.Sizes);
    }

    // Mirrors the offline branch of SpecialMappingOrchestrator.ResolveAsync (Gemini off or no key).
    private static SpecialMappingResult ResolveSpecialsOffline(
        IReadOnlyList<(string RelativePath, string FileName)> files,
        IReadOnlyList<TrackedEpisode> specials,
        IReadOnlyList<TrackedEpisode> regularEpisodes)
    {
        var tree = PackFolderTreeAnalyzer.Analyze(files.Select(file => file.RelativePath).ToList());
        var request = PackSpecialPayloadBuilder.Build(files, tree, specials, "Show", 1, regularEpisodes);
        if (request.Context.CandidateCount == 0)
        {
            return new SpecialMappingResult();
        }

        var result = PackSpecialPayloadBuilder.ApplyProposals(request, specials, SpecialMappingSource.Rule);
        SpecialMappingValidator.Validate(result, specials);
        return result;
    }

    public static PackFileEntry Entry(PackTorrentInventory inventory, string relativePath) =>
        inventory.Files.Single(file => string.Equals(file.RelativePath, relativePath, StringComparison.Ordinal));

    public static void AssertRegular(PackTorrentInventory inventory, string relativePath, int season, int episode)
    {
        var entry = Entry(inventory, relativePath);
        Assert.True(
            entry.Classification == PackFileClassification.RegularEpisode &&
            entry.MatchedSeasonNumber == season &&
            entry.MatchedEpisodeNumber == episode,
            $"Expected '{relativePath}' -> RegularEpisode S{season:00}E{episode:00}, got {entry.Classification} " +
            $"S{entry.MatchedSeasonNumber:00}E{entry.MatchedEpisodeNumber:00} ({entry.MatchReason}).");
    }

    private static List<TrackedEpisode> BuildEpisodes(PackScenarioDefinition scenario)
    {
        var episodes = new List<TrackedEpisode>();
        foreach (var (season, count) in scenario.EpisodesPerSeason)
        {
            for (var episode = 1; episode <= count; episode++)
            {
                episodes.Add(new TrackedEpisode
                {
                    SeasonNumber = season,
                    EpisodeNumber = episode,
                    Title = $"Episode {episode}",
                    AirDate = scenario.AirDates?.TryGetValue((season, episode), out var airDate) == true
                        ? DateTime.Parse(airDate, CultureInfo.InvariantCulture)
                        : null
                });
            }
        }

        foreach (var (episode, airDate, title) in scenario.Specials ?? [])
        {
            episodes.Add(new TrackedEpisode
            {
                SeasonNumber = 0,
                EpisodeNumber = episode,
                Title = title,
                AirDate = DateTime.Parse(airDate, CultureInfo.InvariantCulture)
            });
        }

        return episodes;
    }
}
