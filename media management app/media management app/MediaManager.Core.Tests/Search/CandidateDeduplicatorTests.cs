using FluentAssertions;
using media_management_app.Services;

namespace MediaManager.Core.Tests.Search;

public class CandidateDeduplicatorTests
{
    private sealed record Row(string Name, long Size, string Url, int Seeders);

    private const string Hash = "0123456789abcdef0123456789abcdef01234567";
    private const long Mb = 1024L * 1024L;

    private static List<Row> Dedupe(IEnumerable<Row> rows, bool fuzzy, int toleranceMb = 5) =>
        CandidateDeduplicator.Deduplicate(rows, r => r.Name, r => r.Size, r => r.Url, r => r.Seeders, fuzzy, toleranceMb);

    [Fact]
    public void Exact_SameInfoHashFromTwoEngines_IsOneCandidateWithMostSeeders()
    {
        // piratebay and thepiratebay return the same rows with differently built magnet links.
        var rows = new[]
        {
            new Row("Movie 2014 1080p", 2000 * Mb, $"magnet:?xt=urn:btih:{Hash}&dn=Movie&tr=udp://a", 10),
            new Row("Movie 2014 1080p", 2000 * Mb, $"magnet:?xt=urn:btih:{Hash.ToUpperInvariant()}&tr=udp://b&tr=udp://c", 25)
        };

        Dedupe(rows, fuzzy: false).Should().ContainSingle().Which.Seeders.Should().Be(25);
    }

    [Fact]
    public void Exact_Base32InfoHash_EqualsHex()
    {
        // Same 20-byte hash written in base32 (32 chars) and hex (40 chars).
        CandidateDeduplicator.TryGetInfoHash("magnet:?xt=urn:btih:AERUKZ4JVPG66AJDIVTYTK6N54ASGRLH")
            .Should().Be("0123456789abcdef0123456789abcdef01234567");
    }

    [Fact]
    public void Exact_DifferentTorrents_AreKept()
    {
        var rows = new[]
        {
            new Row("Movie 2014 1080p x264", 2000 * Mb, "https://site/a.torrent", 10),
            new Row("Movie 2014 1080p x265", 1500 * Mb, "https://site/b.torrent", 10)
        };

        Dedupe(rows, fuzzy: false).Should().HaveCount(2);
    }

    [Fact]
    public void Fuzzy_SameReleaseDifferentSeparatorsAndCrc_IsMerged()
    {
        var rows = new[]
        {
            new Row("Coherence.2013.1080p.BluRay.x264-GROUP", 1500 * Mb, "https://a/1.torrent", 5),
            new Row("Coherence 2013 1080p BluRay x264-GROUP [0FBE586A].mkv", 1503 * Mb, "https://b/2.torrent", 9)
        };

        Dedupe(rows, fuzzy: true).Should().ContainSingle().Which.Seeders.Should().Be(9);
    }

    [Fact]
    public void Fuzzy_SizesAcrossOldBucketBoundary_AreStillMerged()
    {
        // The old fixed 5 MB buckets put 4.99 MB and 5.01 MB in different buckets.
        var rows = new[]
        {
            new Row("Show - 05 (1080p)", (long)(4.99 * Mb), "https://a/1", 1),
            new Row("Show - 05 (1080p)", (long)(5.01 * Mb), "https://a/2", 2)
        };

        Dedupe(rows, fuzzy: true).Should().ContainSingle();
    }

    [Fact]
    public void Fuzzy_DifferentGroups_AreNotMerged()
    {
        // The old normalizer removed every [..] tag, release group included.
        var rows = new[]
        {
            new Row("[SubsPlease] Kanata Kara - 06 (1080p) [ABCDEF12].mkv", 1400 * Mb, "https://a/1", 50),
            new Row("[Erai-raws] Kanata Kara - 06 (1080p) [12345678].mkv", 1401 * Mb, "https://a/2", 40)
        };

        Dedupe(rows, fuzzy: true).Should().HaveCount(2);
    }

    [Fact]
    public void Fuzzy_SameNameButSizeBeyondTolerance_IsKept()
    {
        var rows = new[]
        {
            new Row("Movie 2014 1080p", 2000 * Mb, "https://a/1", 1),
            new Row("Movie 2014 1080p", 2100 * Mb, "https://a/2", 1)
        };

        Dedupe(rows, fuzzy: true).Should().HaveCount(2);
    }

    [Fact]
    public void KeepsFirstSeenOrder()
    {
        var rows = new[]
        {
            new Row("B", 1, "https://x/b", 1),
            new Row("A", 1, "https://x/a", 1),
            new Row("B", 1, "https://x/b", 5)
        };

        Dedupe(rows, fuzzy: false).Select(r => r.Name).Should().Equal("B", "A");
    }
}
