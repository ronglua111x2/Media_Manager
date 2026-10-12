using FluentAssertions;
using media_management_app.Services;

namespace MediaManager.Core.Tests.Pack;

public class SeasonPackCleanupMatcherTests
{
    private static readonly SeasonPackCleanupScope MultiSeasonScope = SeasonPackCleanupMatcher.Resolve(
        ownerSeasonNumber: 1,
        coveredSeasonsValue: "1,2,3",
        packTorrentHash: "pack-hash",
        lastPackLinkTorrentHash: "pack-hash");

    [Fact]
    public void Resolve_IncludesOwnerAndCoveredSeasons()
    {
        MultiSeasonScope.OwnerSeasonNumber.Should().Be(1);
        MultiSeasonScope.CoveredSeasonNumbers.Should().Equal(1, 2, 3);
        MultiSeasonScope.CoveredSeasonsDisplay.Should().Be("S01, S02, S03");
        MultiSeasonScope.TorrentHashes.Should().Equal("pack-hash");
    }

    [Fact]
    public void Resolve_EmptyCoverage_FallsBackToOwner()
    {
        var scope = SeasonPackCleanupMatcher.Resolve(6, null, "abc", null);

        scope.CoveredSeasonNumbers.Should().Equal(6);
        scope.CoveredSeasonsDisplay.Should().Be("S06");
    }

    [Fact]
    public void Resolve_AddsOwnerWhenMissingFromCoverage()
    {
        var scope = SeasonPackCleanupMatcher.Resolve(1, "2,3", "abc", "def");

        scope.CoveredSeasonNumbers.Should().Equal(1, 2, 3);
        scope.TorrentHashes.Should().Equal("abc", "def");
    }

    [Fact]
    public void SelectItems_MultiSeasonOwnerAndCoveredEpisodes_AreCaptured()
    {
        var items = new[]
        {
            PackEpisode(id: 1, owner: 1, seasonHint: 1),
            PackEpisode(id: 2, owner: 1, seasonHint: 2),
            PackEpisode(id: 3, owner: 1, seasonHint: 3)
        };

        SeasonPackCleanupMatcher.SelectItems(items, MultiSeasonScope, EmptyPaths())
            .Select(item => item.Id)
            .Should()
            .Equal(1, 2, 3);
    }

    [Fact]
    public void SelectItems_MatchedSpecialAndOrphan_AreCaptured()
    {
        var items = new[]
        {
            PackEpisode(id: 10, owner: 1, seasonHint: 0),
            new SeasonPackCleanupCandidate
            {
                Id = 11,
                PackOwnerSeasonNumber = 1,
                IsOrphanPackSpecial = true,
                IsSeasonPackLink = true,
                TorrentHash = "pack-hash",
                FilePath = @"D:\Downloads\Show\extras\ova.mkv"
            }
        };

        SeasonPackCleanupMatcher.SelectItems(items, MultiSeasonScope, EmptyPaths())
            .Select(item => item.Id)
            .Should()
            .Equal(10, 11);
    }

    [Fact]
    public void SelectItems_AfterUnlinkPreservesProvenance_StillCapturesCoveredSeasons()
    {
        var items = new[]
        {
            PackEpisode(id: 21, owner: 1, seasonHint: 1),
            PackEpisode(id: 22, owner: 1, seasonHint: 2)
        };

        SeasonPackCleanupMatcher.SelectItems(items, MultiSeasonScope, EmptyPaths())
            .Select(item => item.Id)
            .Should()
            .Equal(21, 22);
    }

    [Fact]
    public void SelectItems_LinkedPackWithoutPriorUnlink_IsCaptured()
    {
        var items = new[]
        {
            PackEpisode(id: 31, owner: 1, seasonHint: 1),
            PackEpisode(id: 32, owner: 1, seasonHint: 2)
        };

        SeasonPackCleanupMatcher.SelectItems(items, MultiSeasonScope, EmptyPaths())
            .Should()
            .HaveCount(2);
    }

    [Fact]
    public void SelectItems_UnrelatedEpisodeAndImport_AreRetained()
    {
        var items = new[]
        {
            PackEpisode(id: 41, owner: 1, seasonHint: 1),
            new SeasonPackCleanupCandidate
            {
                Id = 42,
                IsSeasonPackLink = false,
                TorrentHash = "episode-hash",
                FilePath = @"D:\Downloads\Show\S01E99-episode.mkv"
            },
            new SeasonPackCleanupCandidate
            {
                Id = 43,
                IsExternalImport = true,
                PackOwnerSeasonNumber = 1,
                FilePath = @"D:\Library\Show\S01E01.mkv"
            }
        };

        SeasonPackCleanupMatcher.SelectItems(items, MultiSeasonScope, EmptyPaths())
            .Select(item => item.Id)
            .Should()
            .Equal(41);
    }

    [Fact]
    public void SelectItems_LegacyClearedProvenance_RecoversByExactPackPath()
    {
        var leftover = new SeasonPackCleanupCandidate
        {
            Id = 51,
            FilePath = @"D:\Downloads\Show\Season 02\S02E01.mkv"
        };
        var otherSeasonFile = new SeasonPackCleanupCandidate
        {
            Id = 52,
            FilePath = @"D:\Downloads\Other\S02E01.mkv"
        };
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            SeasonPackCleanupMatcher.NormalizePath(@"D:\Downloads\Show\Season 02\S02E01.mkv")
        };

        SeasonPackCleanupMatcher.SelectItems([leftover, otherSeasonFile], MultiSeasonScope, paths)
            .Select(item => item.Id)
            .Should()
            .Equal(51);
    }

    [Fact]
    public void MatchesLegacyExactPath_DoesNotUseBareSeasonNumber()
    {
        var leftover = new SeasonPackCleanupCandidate
        {
            Id = 61,
            FilePath = @"D:\Downloads\Show\S02E01.mkv"
        };

        SeasonPackCleanupMatcher.MatchesLegacyExactPath(leftover, MultiSeasonScope, EmptyPaths())
            .Should()
            .BeFalse();
    }

    [Fact]
    public void MatchesProvenance_HashMatchRequiresPackAssociation()
    {
        var episodeOnly = new SeasonPackCleanupCandidate
        {
            Id = 71,
            TorrentHash = "pack-hash",
            FilePath = @"D:\Downloads\Show\S01E01.mkv"
        };
        var packLinked = episodeOnly with { IsSeasonPackLink = true };

        SeasonPackCleanupMatcher.MatchesProvenance(episodeOnly, MultiSeasonScope).Should().BeFalse();
        SeasonPackCleanupMatcher.MatchesProvenance(packLinked, MultiSeasonScope).Should().BeTrue();
    }

    [Fact]
    public void SelectItems_SecondPassAfterDelete_IsEmpty()
    {
        var items = new[]
        {
            PackEpisode(id: 81, owner: 1, seasonHint: 1),
            PackEpisode(id: 82, owner: 1, seasonHint: 2)
        };

        var first = SeasonPackCleanupMatcher.SelectItems(items, MultiSeasonScope, EmptyPaths());
        first.Should().HaveCount(2);

        var remaining = items.Where(item => first.All(captured => captured.Id != item.Id));
        SeasonPackCleanupMatcher.SelectItems(remaining, MultiSeasonScope, EmptyPaths())
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void MatchesProvenance_PartialFailureMix_StillSelectsUnlockedRows()
    {
        var unlocked = PackEpisode(id: 91, owner: 1, seasonHint: 2);
        var locked = PackEpisode(id: 92, owner: 1, seasonHint: 3);

        SeasonPackCleanupMatcher.SelectItems([unlocked, locked], MultiSeasonScope, EmptyPaths())
            .Select(item => item.Id)
            .Should()
            .Equal(91, 92);
    }

    private static SeasonPackCleanupCandidate PackEpisode(long id, int owner, int seasonHint) =>
        new()
        {
            Id = id,
            PackOwnerSeasonNumber = owner,
            IsSeasonPackLink = true,
            TorrentHash = "pack-hash",
            FilePath = $@"D:\Downloads\Show\S{seasonHint:00}E01.mkv"
        };

    private static HashSet<string> EmptyPaths() => new(StringComparer.OrdinalIgnoreCase);
}
