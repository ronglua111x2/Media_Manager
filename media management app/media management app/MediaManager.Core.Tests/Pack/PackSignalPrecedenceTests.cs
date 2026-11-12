using FluentAssertions;
using media_management_app.Services;

namespace MediaManager.Core.Tests.Pack;

// PR-8 (PARSE-03/04/08): weak signals (folder ranges, keywords, the word "movie") must not
// override an explicit SxxEyy.
public class PackSignalPrecedenceTests
{
    [Theory]
    [InlineData("Season 02", 2)]
    [InlineData("Season 2", 2)]
    [InlineData("Show (Season 2) [BD 1080p]", 2)]
    [InlineData("Show Season 6 S06 (1080p WEB-DL)", 6)]
    [InlineData("S02", 2)]
    [InlineData("[Group] Show S2", 2)]
    [InlineData("Season 1 Part 1", 1)]
    [InlineData("Show Season 1-3 1080p", null)]
    [InlineData("Show Season 1~3", null)]
    [InlineData("Show Seasons 1 & 2", null)]
    [InlineData("Show Seasons 1", null)]
    [InlineData("Show Season 1 and Season 2", null)]
    [InlineData("Show S01-S03", null)]
    [InlineData("Show.S01-S03.1080p", null)]
    [InlineData("Season 1 Part 2", null)]
    [InlineData("Season 2 Cour 2", null)]
    [InlineData("Show Complete Series", null)]
    [InlineData("Show.Complete", null)]
    // Audit L1-L1: an episode range after the season is not a season range.
    [InlineData("[Group] Show Season 2 - 13-24 [1080p]", 2)]
    [InlineData("Show Season 1 - 01~12", 1)]
    [InlineData("Show Season 2 - 13~24 (BD)", 2)]
    // Audit L1-L2: non-ASCII digits must not throw.
    [InlineData("Show Season ２", null)]
    public void TryParseSeasonFolderSegment_OnlyASingleSeason(string segment, int? expected)
    {
        TorrentCandidateParser.TryParseSeasonFolderSegment(segment).Should().Be(expected);
    }

    [Theory]
    [InlineData("Show/Season 1 Part 2/Show.S01E03.mkv", 1, 2)]
    [InlineData("Show/Season 2 Cour 2/Show - 01.mkv", 2, 2)]
    [InlineData("Show/Season 1 Part 1/Show.S01E03.mkv", null, null)]
    [InlineData("Show/Season 1/Show.S01E03.mkv", null, null)]
    public void TryGetSeasonPartFromPath_ReadsPartTwoAndLater(string relativePath, int? season, int? part)
    {
        var result = TorrentCandidateParser.TryGetSeasonPartFromPath(relativePath);

        (result?.Season, result?.Part).Should().Be((season, part));
    }

    [Theory]
    [InlineData("Show/Movies/Show The First.mkv", true)]
    [InlineData("Show/Film/Show The First.mkv", true)]
    [InlineData("Show/Show.The.Movie.2019.1080p.mkv", true)]
    [InlineData("Show/[Grp] Show Movie - Red Night [1080p].mkv", true)]
    [InlineData("Show/Season 3/Show.S03E04.Movie.Night.1080p.mkv", false)]
    [InlineData("Moviepass Show/Season 1/Moviepass.Show.S01E02.mkv", false)]
    [InlineData("Show/Season 1/Show.S01E02.Film.Club.mkv", false)]
    [InlineData("Show/Season 1/Show.S01E02.mkv", false)]
    public void PackMovieRules_WordLevelAndNotForSxxEyy(string relativePath, bool expected)
    {
        PackMovieRules.IsMovieFile(relativePath, Path.GetFileName(relativePath)).Should().Be(expected);
    }

    [Theory]
    [InlineData("Show/Season 1/Show.OVA.02.mkv", true)]
    [InlineData("Show/Season 1/Show OVA - 02.mkv", true)]
    [InlineData("Show/Season 1/Show.S01E05.Special.1.mkv", true)]
    [InlineData("Show/Season 1/Show.Special.mkv", true)]
    [InlineData("Show/Specials/Show.S01E05.mkv", true)]
    [InlineData("Show.S00E05.Christmas.Special.mkv", true)]
    [InlineData("Show/Season 1/Show.S01E05.The.Special.mkv", false)]
    [InlineData("Show/Season 1/Show.S01E05.OVA.Night.mkv", false)]
    [InlineData("Special Ops/Season 1/Special.Ops.S01E05.mkv", false)]
    public void SpecialKeyword_OnlyWithoutRegularEpisodeOrNextToNumber(string relativePath, bool expected)
    {
        TorrentCandidateParser.Parse(Path.GetFileName(relativePath), relativePath).IsSpecialContent.Should().Be(expected);
    }

    // Fuzz from the audit (section 4a): 221/225 real names became specials once "Special" was added
    // to the episode title. Generated names, fixed list, so the run is deterministic.
    [Fact]
    public void Fuzz_SpecialInEpisodeTitle_KeepsSeasonEpisodeAndRegularGroup()
    {
        var shows = new[] { "Show", "Show.Name", "The.Show", "Show.2019", "[Grp] Show -", "Show Name" };
        var tails = new[] { ".1080p.WEB-DL.x264-GRP.mkv", " [1080p].mkv", ".720p.HDTV.mkv", ".mkv" };
        var failures = new List<string>();
        foreach (var show in shows)
        {
            foreach (var tail in tails)
            {
                for (var season = 1; season <= 3; season++)
                {
                    for (var episode = 1; episode <= 12; episode += 5)
                    {
                        var fileName = $"{show} S{season:00}E{episode:00}.The.Special{tail}";
                        var relativePath = $"Pack/Season {season}/{fileName}";
                        var parsed = TorrentCandidateParser.Parse(fileName, relativePath);
                        if (parsed.SeasonNumber != season ||
                            parsed.EpisodeNumber != episode ||
                            parsed.IsSpecialContent ||
                            PackSeasonFileGrouper.IsExcludedFromEpisodeGroup(relativePath, fileName))
                        {
                            failures.Add(fileName);
                        }
                    }
                }
            }
        }

        failures.Should().BeEmpty();
    }

    [Fact]
    public void Group_SxxEyyInFileNameOutranksSeasonFolder()
    {
        var files = new List<(string, string)>
        {
            ("Show/Season 2/Show.S01E05.mkv", "Show.S01E05.mkv"),
            ("Show/Season 2/Show.S02E01.mkv", "Show.S02E01.mkv"),
            ("Show Season 1-3/Show.S03E02.mkv", "Show.S03E02.mkv"),
        };

        var groups = PackSeasonFileGrouper.Group(files, tree: null);

        groups.Select(group => (group.SeasonNumber, group.Files.Count)).Should().Equal((1, 1), (2, 1), (3, 1));
    }
}
