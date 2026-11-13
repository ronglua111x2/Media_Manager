using MediaManager.App.Tests.Special.Fixtures;
using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.App.Tests.Special;

/// <summary>Season air-date windows over TMDB Season 0 (parser audit 10.3 step 1, strict variant of 10.4).</summary>
public class SpecialSeasonWindowMapperTests
{
    [Fact]
    public void BuildBlocks_SplitsSeasonZeroByEachSeasonsAirDates()
    {
        var pack = SpecialCases.TwoSeasonBrassPack();

        var blocks = SpecialSeasonWindowMapper.BuildBlocks(pack.RegularEpisodes, pack.Specials);

        Assert.Equal([1, 2, 3], blocks.Select(block => block.SeasonNumber));
        Assert.Equal(Enumerable.Range(1, 8), blocks[0].Entries.Select(entry => entry.EpisodeNumber));
        Assert.Equal(Enumerable.Range(9, 11), blocks[1].Entries.Select(entry => entry.EpisodeNumber));
        Assert.Equal(Enumerable.Range(20, 13), blocks[2].Entries.Select(entry => entry.EpisodeNumber));
    }

    [Fact]
    public void BuildBlocks_EntriesBeforeSeasonOneJoinSeasonOneAndKeepTmdbNumberOrder()
    {
        var pack = SpecialCases.ByName("rooftop-complete-minis");

        var blocks = SpecialSeasonWindowMapper.BuildBlocks(pack.RegularEpisodes, pack.Specials);

        var block = Assert.Single(blocks);
        Assert.Equal(Enumerable.Range(1, 32), block.Entries.Select(entry => entry.EpisodeNumber));
    }

    [Fact]
    public void BuildBlocks_EntryWithoutAirDate_IsLeftOut()
    {
        var regular = new[] { SpecialShowBuilder.Episode(1, 1, "Episode 1", new DateTime(2020, 1, 1)) };
        var specials = new[]
        {
            SpecialShowBuilder.Episode(0, 1, "Dated", new DateTime(2020, 2, 1)),
            SpecialShowBuilder.Episode(0, 2, "Undated", null)
        };

        var blocks = SpecialSeasonWindowMapper.BuildBlocks(regular, specials);

        Assert.Equal([1], Assert.Single(blocks).Entries.Select(entry => entry.EpisodeNumber));
    }

    [Fact]
    public void Map_ExactBlockWithOneUnnumberedFile_MarksOnlyThatFileInferred()
    {
        var pack = SpecialCases.TwoSeasonBrassPack();
        var request = Build(pack);
        var blocks = SpecialSeasonWindowMapper.BuildBlocks(pack.RegularEpisodes, pack.Specials);

        var mapping = SpecialSeasonWindowMapper.Map(request.Context.Candidates, blocks, new HashSet<int>(), new HashSet<int>());

        var inferred = Assert.Single(mapping.Assignments, assignment => assignment.IsInferred);
        Assert.Equal(SpecialCases.BrassOva, request.Context.Candidates[inferred.CandidateIndex].RelativePath);
        Assert.Equal(8, inferred.Episode.EpisodeNumber);
        Assert.Equal(8, mapping.Assignments.Count);
    }

    [Fact]
    public void Map_RepeatedSpecialNumbers_AppliesNothing()
    {
        var regular = new[] { SpecialShowBuilder.Episode(1, 1, "Episode 1", new DateTime(2020, 1, 1)) };
        var specials = new[]
        {
            SpecialShowBuilder.Episode(0, 1, "First", new DateTime(2020, 2, 1)),
            SpecialShowBuilder.Episode(0, 2, "Second", new DateTime(2020, 3, 1))
        };
        var blocks = SpecialSeasonWindowMapper.BuildBlocks(regular, specials);
        SpecialMappingCandidate[] candidates =
        [
            new() { RelativePath = "Show/Specials/a.mkv", FileName = "a.mkv", ParentSeason = 1, LocalIndex = 1, NamingPattern = SpecialMappingNamingPattern.Opaque },
            new() { RelativePath = "Show/Specials/b.mkv", FileName = "b.mkv", ParentSeason = 1, LocalIndex = 1, NamingPattern = SpecialMappingNamingPattern.Opaque }
        ];

        var mapping = SpecialSeasonWindowMapper.Map(candidates, blocks, new HashSet<int>(), new HashSet<int>());

        Assert.Empty(mapping.Assignments);
        Assert.Empty(mapping.Suggestions);
        Assert.Equal(2, mapping.UnresolvedReasons.Count);
    }

    [Fact]
    public async Task TwoSeasonPack_RulesOnly_SeasonTwoGetsSuggestionsThatAreNotLinked()
    {
        var pack = SpecialCases.TwoSeasonBrassPack();
        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."));

        var result = await host.ResolveAsync(pack, useGemini: false);

        var seasonTwo = result.Items.Where(item => item.RelativePath.StartsWith(SpecialCases.BrassS2, StringComparison.Ordinal)).ToList();
        Assert.Equal(7, seasonTwo.Count);
        Assert.All(seasonTwo, item => Assert.Null(item.Episode));
        Assert.Equal(
            Enumerable.Range(9, 7),
            seasonTwo.OrderBy(item => item.RelativePath, StringComparer.Ordinal).Select(item => item.SuggestedEpisode!.EpisodeNumber));
        Assert.All(seasonTwo, item => Assert.Contains("11 TMDB special(s) for 7 file(s)", item.Reason, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TwoSeasonPack_RulesOnly_SeasonOneSourcesAreWindowAndInferred()
    {
        var pack = SpecialCases.TwoSeasonBrassPack();
        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."));

        var result = await host.ResolveAsync(pack, useGemini: false);

        Assert.Equal(SpecialMappingProposalReason.Inferred, result.ByPath[SpecialCases.BrassOva].ProposalReason);
        Assert.Equal(
            7,
            result.Items.Count(item => item.RelativePath.StartsWith(SpecialCases.BrassS1, StringComparison.Ordinal) &&
                                       item.ProposalReason == SpecialMappingProposalReason.Window));
    }

    /// <summary>Note R8: half the bucket being "OVA - N" must not make the odd file an OVA-dash file with index 0.</summary>
    [Fact]
    public void Build_OddFileInOvaDashBucket_KeepsItsOwnStyle()
    {
        var pack = new SpecialShowBuilder("Lantern Knight", 900021)
            .Special(1, "OVA 1: Hot Spring", new DateTime(2015, 1, 1))
            .Special(2, "OVA 2: Beach Day", new DateTime(2015, 2, 1))
            .Special(3, "Making of the Lantern", new DateTime(2015, 3, 1))
            .SpecialFile("Knight/Specials/Lantern Knight OVA - 01.mkv", 1)
            .SpecialFile("Knight/Specials/Lantern Knight OVA - 02.mkv", 2)
            .SpecialFile("Knight/Specials/Lantern Knight - Making of the Lantern.mkv", 3)
            .Build("mixed-ova-bucket", "R8");

        var request = Build(pack);

        Assert.Equal(
            SpecialMappingNamingPattern.Opaque,
            request.Context.Candidates.Single(candidate => candidate.FileName.Contains("Making", StringComparison.Ordinal)).NamingPattern);
    }

    [Fact]
    public async Task OddFileInOvaDashBucket_RulesOnly_MatchedByItsTitle()
    {
        var pack = new SpecialShowBuilder("Lantern Knight", 900021)
            .Special(1, "OVA 1: Hot Spring", new DateTime(2015, 1, 1))
            .Special(2, "OVA 2: Beach Day", new DateTime(2015, 2, 1))
            .Special(3, "Making of the Lantern", new DateTime(2015, 3, 1))
            .SpecialFile("Knight/Specials/Lantern Knight OVA - 01.mkv", 1)
            .SpecialFile("Knight/Specials/Lantern Knight OVA - 02.mkv", 2)
            .SpecialFile("Knight/Specials/Lantern Knight - Making of the Lantern.mkv", 3)
            .Build("mixed-ova-bucket", "R8");
        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."));

        var result = await host.ResolveAsync(pack, useGemini: false);

        Assert.Equal(
            pack.ExpectedS00E.ToDictionary(pair => pair.Key, pair => pair.Value!.Value),
            result.Items.Where(item => item.Episode is not null).ToDictionary(item => item.RelativePath, item => item.Episode!.EpisodeNumber));
    }

    private static SpecialMappingAIRequest Build(SpecialCase pack) =>
        PackSpecialPayloadBuilder.Build(
            pack.Files,
            PackFolderTreeAnalyzer.Analyze(pack.Files.Select(file => file.RelativePath).ToList()),
            pack.Specials,
            pack.ShowTitle,
            pack.TmdbId,
            pack.RegularEpisodes);
}
