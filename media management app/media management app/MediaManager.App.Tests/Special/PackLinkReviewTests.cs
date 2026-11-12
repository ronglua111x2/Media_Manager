using media_management_app.Models;
using media_management_app.ViewModels;

namespace MediaManager.App.Tests.Special;

/// <summary>The pack link review window must show enough to catch a wrong special mapping (appendix A, note R2).</summary>
public class PackLinkReviewTests
{
    [Fact(Skip = "PARSE-09: review lists skipped files (R2)")]
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

        Assert.Contains(viewModel.Rows, row => row.FileName == "Show - Menu.mkv");
    }

    [Fact(Skip = "PARSE-09: review shows inventory warnings (R2)")]
    public void LoadPreview_InventoryWarning_IsShown()
    {
        var preview = Preview();
        preview.Inventory.Warnings.Add("Movies detected — add separately in Movies library.");

        var viewModel = new PackLinkReviewViewModel(preview);

        Assert.Contains("Movies detected — add separately in Movies library.", viewModel.Warnings);
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
