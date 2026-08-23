using FluentAssertions;
using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.Core.Tests.Pack;

public class PackSeasonFileGrouperTests
{
    [Fact]
    public void Group_MultiSeasonFolders_SplitsBySeason()
    {
        var groups = PackSeasonFileGrouper.Group(LoadGoldenTree("multi-season-plus-extras"), tree: null);

        groups.Select(g => g.SeasonNumber).Should().BeEquivalentTo([1, 2]);
        groups.Single(g => g.SeasonNumber == 1).Files.Should().HaveCount(2);
        groups.Single(g => g.SeasonNumber == 2).Files.Should().HaveCount(1);
    }

    [Fact]
    public void Group_ExtrasFolder_IsExcluded()
    {
        var groups = PackSeasonFileGrouper.Group(SampleTree(), tree: null);

        groups.SelectMany(g => g.Files).Should().NotContain(f => f.FileName == "trailer.mkv");
    }

    [Fact]
    public void IsExcludedFromEpisodeGroup_MoviesFolder_IsTrue()
    {
        PackSeasonFileGrouper.IsExcludedFromEpisodeGroup("movies/feature.mkv", "feature.mkv")
            .Should().BeTrue();
        PackSeasonFileGrouper.GetEpisodeGroupExcludeReason("movies/feature.mkv", "feature.mkv")
            .Should().Be("movie");
    }

    [Fact]
    public void IsExcludedFromEpisodeGroup_FilenameContainsMovie_IsTrue()
    {
        PackSeasonFileGrouper.IsExcludedFromEpisodeGroup("Season 01/Show Movie.mkv", "Show Movie.mkv")
            .Should().BeTrue();
    }

    [Fact]
    public void Group_UsesPathSeasonHints_WhenTreeProvided()
    {
        var files = new List<(string RelativePath, string FileName)>
        {
            ("DiscA/ep1.mkv", "ep1.mkv"),
            ("DiscB/ep2.mkv", "ep2.mkv")
        };
        var tree = new PackFolderTreeAnalysis
        {
            PathSeasonHints = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase)
            {
                ["DiscA/ep1.mkv"] = 1,
                ["DiscB/ep2.mkv"] = 2
            }
        };

        var groups = PackSeasonFileGrouper.Group(files, tree);

        groups.Should().HaveCount(2);
        groups.Single(g => g.SeasonNumber == 1).Files.Should().ContainSingle(f => f.FileName == "ep1.mkv");
        groups.Single(g => g.SeasonNumber == 2).Files.Should().ContainSingle(f => f.FileName == "ep2.mkv");
    }

    [Fact]
    public void Group_FlatSxxInFilename_GoesToParsedSeason()
    {
        var files = new List<(string RelativePath, string FileName)>
        {
            ("Show - S04E01.mkv", "Show - S04E01.mkv"),
            ("Show - S04E02.mkv", "Show - S04E02.mkv")
        };

        var groups = PackSeasonFileGrouper.Group(files, tree: null);

        groups.Should().ContainSingle();
        groups[0].SeasonNumber.Should().Be(4);
        groups[0].Files.Should().HaveCount(2);
    }

    private static IReadOnlyList<(string RelativePath, string FileName)> LoadGoldenTree(string id)
    {
        var files = MediaManager.Core.Tests.Fixtures.GoldenFixtureFile.Root("pack-file-trees.json")
            .GetProperty("cases")
            .EnumerateArray()
            .First(item => item.GetProperty("id").GetString() == id)
            .GetProperty("files");

        return files.EnumerateArray()
            .Select(file => (file.GetProperty("relativePath").GetString()!, file.GetProperty("fileName").GetString()!))
            .ToList();
    }

    private static IReadOnlyList<(string RelativePath, string FileName)> SampleTree() =>
        LoadGoldenTree("multi-season-plus-extras");
}
