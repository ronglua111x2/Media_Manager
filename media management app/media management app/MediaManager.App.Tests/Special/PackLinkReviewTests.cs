using MediaManager.App.Tests.Special.Fixtures;
using media_management_app.Models;
using media_management_app.ViewModels;

namespace MediaManager.App.Tests.Special;

/// <summary>The pack link review window must show enough to catch a wrong special mapping (appendix A, note R2).</summary>
public class PackLinkReviewTests
{
    [Fact]
    public void LoadPreview_SkippedFile_GetsARow()
    {
        var viewModel = new PackLinkReviewViewModel(Preview(
            new PackFileEntry
            {
                RelativePath = "Pack/Season 01/Show - Menu.mkv",
                FileName = "Show - Menu.mkv",
                Classification = PackFileClassification.Skipped,
                MatchReason = "No tracked episode match."
            }));

        var row = Assert.Single(viewModel.Rows, row => row.FileName == "Show - Menu.mkv");
        Assert.True(row.IsSkipped);
        Assert.Equal("No tracked episode match.", row.Reason);
    }

    [Fact]
    public void LoadPreview_InventoryWarning_IsShown()
    {
        var preview = Preview();
        preview.Inventory.Warnings.Add("Movies detected — add separately in Movies library.");

        var viewModel = new PackLinkReviewViewModel(preview);

        Assert.Contains("Movies detected — add separately in Movies library.", viewModel.Warnings);
    }

    /// <summary>
    /// The recorded wrong Gemini answer: with the TMDB title and air date next to the file, "S1S07 → Practice Room
    /// Days #1 (2015-06-17)" stands out as wrong; with only "S00E01" it did not.
    /// </summary>
    [Fact]
    public async Task LoadPreview_GeminiMapping_RowsShowTmdbTitleAirDateAndSource()
    {
        var pack = SpecialCases.TwoSeasonBrassPack();
        using var host = new SpecialOrchestratorHost(FakeGeminiResponses.SeasonTwoLastDiffersFromSuggestion);
        var service = SpecialPackPrepareHost.Create(pack, host.Orchestrator);
        var preview = await service.PrepareSeasonPackLinkAsync(SpecialPackPrepareHost.ShowId, 1, useGeminiForSpecials: true);

        var viewModel = new PackLinkReviewViewModel(preview);

        // A Gemini pair that differs from the season-window suggestion: with the TMDB title and air date next to
        // the file, S2S07 to "Harbor Brass Movie: Encore, Part 1 (2017-09-13)" stands out, and the row is flagged.
        var row = viewModel.Rows.Single(item => item.FileName == "[Grp] Harbor Brass - S2S07.mkv");
        Assert.Equal("S00E16", row.TargetLabel);
        Assert.Equal("Harbor Brass Movie: Encore, Part 1", row.TmdbTitle);
        Assert.Equal("2017-09-13", row.AirDateLabel);
        Assert.Equal("Gemini AI", row.SourceLabel);
        Assert.True(row.NeedsAttention);
        Assert.Contains("S00E15", row.Reason);
        Assert.True(viewModel.CanRetry);
        Assert.StartsWith("Review AI mappings", viewModel.WindowTitle, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadPreview_RulesOnly_ShowsRuleSourcesAndNoRetry()
    {
        var pack = SpecialCases.TwoSeasonBrassPack();
        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."), geminiEnabled: false);
        var service = SpecialPackPrepareHost.Create(pack, host.Orchestrator);
        var preview = await service.PrepareSeasonPackLinkAsync(SpecialPackPrepareHost.ShowId, 1, useGeminiForSpecials: false);

        var viewModel = new PackLinkReviewViewModel(preview);

        var window = viewModel.Rows.Single(item => item.FileName == "[Grp] Harbor Brass - S1S03.mkv");
        Assert.Equal("Season window", window.SourceLabel);
        Assert.Equal("Practice Room Days #3", window.TmdbTitle);
        Assert.False(window.NeedsAttention);

        var inferred = viewModel.Rows.Single(item => item.FileName == "[Grp] Harbor Brass - S1OVA - Pastry Dash.mkv");
        Assert.Equal("Inferred", inferred.SourceLabel);
        Assert.Equal("Ready, Set, Pastry", inferred.TmdbTitle);
        Assert.True(inferred.NeedsAttention);

        var unmapped = viewModel.Rows.Single(item => item.FileName == "[Grp] Harbor Brass - S2S01.mkv");
        Assert.False(unmapped.IsMatched);
        Assert.True(unmapped.NeedsAttention);
        Assert.Contains("Welcome to the Night Market", unmapped.Reason, StringComparison.Ordinal);

        Assert.False(viewModel.CanRetry);
        Assert.StartsWith("Review special/OVA mappings", viewModel.WindowTitle, StringComparison.Ordinal);
    }

    /// <summary>A pair placed by skipping a recap is labelled as such and still flagged: it is a count, not a name.</summary>
    [Fact]
    public async Task LoadPreview_RecapSkippedRule_LabelledAndFlagged()
    {
        var pack = SpecialCases.ByName("rail-untitled");
        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."), geminiEnabled: false);
        var service = SpecialPackPrepareHost.Create(pack, host.Orchestrator);
        var preview = await service.PrepareSeasonPackLinkAsync(SpecialPackPrepareHost.ShowId, 1, useGeminiForSpecials: false);

        var viewModel = new PackLinkReviewViewModel(preview);

        var row = viewModel.Rows.Single(item => item.FileName == "Night Rail - SP09.mkv");
        Assert.Equal("S00E10", row.TargetLabel);
        Assert.Equal("Window, recaps skipped", row.SourceLabel);
        Assert.Equal("Mini Anime 09 - Do You Think We Arrive", row.TmdbTitle);
        Assert.True(row.NeedsAttention);
        Assert.True(preview.RequiresReview);
    }

    internal static SeasonPackLinkPreview Preview(params PackFileEntry[] files)
    {
        var inventory = new PackTorrentInventory();
        inventory.Files.AddRange(files);
        return new SeasonPackLinkPreview
        {
            Show = new TrackedShow { Id = 1, TmdbId = 900001, Title = "Harbor Brass" },
            OwnerSeason = new TrackedSeason { ShowId = 1, SeasonNumber = 1 },
            Inventory = inventory,
            SpecialMappings = new SpecialMappingResult(),
            RequiresReview = true
        };
    }
}
