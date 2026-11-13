using System.IO;
using media_management_app.Common;

namespace MediaManager.App.Tests.Link;

/// <summary>
/// A file holding two episodes ("S01E01-E02") through the whole link chain: library hardlink, Jellyfin symlink,
/// availability, re-link, reset and duplicates (audit L1-H1, L1-M2, L1-L5, L3-08).
/// </summary>
public sealed class MultiEpisodeLinkStateTests : IDisposable
{
    private const string DoubleFile = "Pack/Season 1/Demo.S01E01E02.mkv";

    private readonly LinkTestHost _host = LinkTestHost.Create();

    public void Dispose() => _host.Dispose();

    // L1-H1: the symlink name was built from a copy of the record without the range.
    [Fact]
    public async Task PackLink_MultiEpisodeFile_JellyfinSymlinkNamesTheRange()
    {
        var showId = PackShow("MH1", DoubleFile);

        await _host.ApplyPackEntriesAsync(showId, 1, LinkTestHost.RegularEntry(DoubleFile, 1, 1, lastEpisode: 2));
        _host.Symlinks.ReconcileAll();

        var symlink = Assert.Single(_host.JellyfinFiles());
        Assert.EndsWith("Demo Show - S01E01-E02.mkv", symlink);
        Assert.EndsWith("Demo Show - S01E01-E02.mkv", Assert.Single(_host.LibraryFiles()));
        Assert.Equal(EpisodeAvailability.Available, _host.Availability(showId, 1, 2));
    }

    // L1-M2 A / L3-08: a pack file linked as S01E01 before ranges existed gets the range on re-link.
    [Fact]
    public async Task PackRelink_FileLinkedWithoutRange_RelinkedUnderTheRangeName()
    {
        _host.FollowHardlinkRemovals();
        var showId = PackShow("MR1", DoubleFile);
        await _host.ApplyPackEntriesAsync(showId, 1, LinkTestHost.RegularEntry(DoubleFile, 1, 1));
        Assert.EndsWith("Demo Show - S01E01.mkv", Assert.Single(_host.LibraryFiles()));

        await _host.ApplyPackEntriesAsync(showId, 1, LinkTestHost.RegularEntry(DoubleFile, 1, 1, lastEpisode: 2));

        Assert.EndsWith("Demo Show - S01E01-E02.mkv", Assert.Single(_host.LibraryFiles()));
        Assert.Equal(2, Assert.Single(_host.Items()).LastEpisodeNumber);
        Assert.Equal(EpisodeAvailability.Available, _host.Availability(showId, 1, 2));
    }

    // L1-L5 a / L3-08: resetting the second episode removes the file that provides it.
    [Fact]
    public async Task ResetSecondEpisodeOfRange_RemovesTheRangeLink()
    {
        var showId = PackShow("MR2", DoubleFile);
        await _host.ApplyPackEntriesAsync(showId, 1, LinkTestHost.RegularEntry(DoubleFile, 1, 1, lastEpisode: 2));

        var result = _host.Link.ResetEpisodeForRedownload(showId, 1, 2);

        Assert.Equal(0, result.ErrorCount);
        Assert.Empty(_host.LibraryFiles());
        Assert.Empty(_host.Items());
        Assert.Equal(EpisodeAvailability.Missing, _host.Availability(showId, 1, 2));
    }

    // L1-L5 b: both episodes already have their own links, so the range file would only duplicate them.
    [Fact]
    public async Task PackLink_EveryEpisodeOfRangeAlreadyLinked_SkipsTheRangeFile()
    {
        var showId = PackShow("MR3", DoubleFile);
        LinkSingle(showId, "S1", "Demo.S01E01.1080p.mkv", 1);
        LinkSingle(showId, "S2", "Demo.S01E02.1080p.mkv", 2);

        var result = await _host.ApplyPackEntriesAsync(showId, 1, LinkTestHost.RegularEntry(DoubleFile, 1, 1, lastEpisode: 2));

        Assert.Equal(0, result.LinkedCount);
        Assert.Equal(2, _host.LibraryFiles().Count);
        Assert.DoesNotContain(_host.LibraryFiles(), path => path.Contains("S01E01-E02"));
    }

    // L3-08: E01 already has a link but E02 has none; the range file is its only source and is linked.
    [Fact]
    public async Task PackLink_OnlyFirstEpisodeAlreadyLinked_StillLinksTheRangeForTheSecond()
    {
        var showId = PackShow("MR4", DoubleFile);
        LinkSingle(showId, "S1", "Demo.S01E01.1080p.mkv", 1);

        var result = await _host.ApplyPackEntriesAsync(showId, 1, LinkTestHost.RegularEntry(DoubleFile, 1, 1, lastEpisode: 2));

        Assert.Equal(1, result.LinkedCount);
        Assert.Contains(_host.LibraryFiles(), path => path.EndsWith("Demo Show - S01E01-E02.mkv"));
        Assert.Equal(EpisodeAvailability.Available, _host.Availability(showId, 1, 2));
    }

    // L1-M2 B: a single-file torrent holding E01 and E02 is linked as the range, whichever episode it was assigned to.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SingleEpisodeTorrent_DoubleFile_LinkedAsTheRange(int assignedEpisode)
    {
        var showId = PackShow("MR5", DoubleFile);
        _host.AddTorrent("SD", "Demo.S01E01E02.1080p.WEB.mkv");
        _host.AssignEpisodeTorrent(showId, 1, assignedEpisode, "SD");

        var result = await _host.Link.LinkEpisodeAsync(showId, 1, assignedEpisode);

        Assert.Equal(1, result.LinkedCount);
        Assert.EndsWith("Demo Show - S01E01-E02.mkv", Assert.Single(_host.LibraryFiles()));
        Assert.Equal(EpisodeAvailability.Available, _host.Availability(showId, 1, 1));
        Assert.Equal(EpisodeAvailability.Available, _host.Availability(showId, 1, 2));
    }

    // Audit R2-A-03: resetting E02 of a single-file S01E01E02 torrent assigned to E01 must not be undone by the next
    // link pass from E01's torrent hash; both episodes are Missing and searchable, and the user is told.
    [Fact]
    public async Task ResetSecondEpisodeOfSingleFileRange_ClearsTheFirstToo_AndIsNotRelinked()
    {
        var showId = PackShow("MR6", DoubleFile);
        _host.AddTorrent("SD2", "Demo.S01E01E02.1080p.WEB.mkv");
        _host.AssignEpisodeTorrent(showId, 1, 1, "SD2");
        Assert.Equal(1, (await _host.Link.LinkEpisodeAsync(showId, 1, 1)).LinkedCount);

        var reset = _host.Link.ResetEpisodeForRedownload(showId, 1, 2);
        var relink = await _host.Link.LinkEpisodeAsync(showId, 1, 1);

        Assert.Contains(reset.Messages, message => message.Contains("S01E01 too", StringComparison.Ordinal));
        Assert.Equal(0, relink.LinkedCount);
        Assert.Empty(_host.LibraryFiles());
        Assert.True(string.IsNullOrEmpty(_host.Db.GetTrackedEpisodes(showId).Single(item => item.SeasonNumber == 1 && item.EpisodeNumber == 1).TorrentHash));
        Assert.Equal(EpisodeAvailability.Missing, _host.Availability(showId, 1, 2));
    }

    private void LinkSingle(long showId, string hash, string fileName, int episode)
    {
        _host.AddTorrent(hash, fileName);
        _host.AssignEpisodeTorrent(showId, 1, episode, hash);
        Assert.Equal(1, _host.Link.LinkEpisodeAsync(showId, 1, episode).GetAwaiter().GetResult().LinkedCount);
    }

    private long PackShow(string hash, params string[] files)
    {
        _host.AddTorrent(hash, files);
        var showId = _host.AddShow(
            5101,
            "Demo Show",
            (1, 1, "Pilot One"),
            (1, 2, "Pilot Two"),
            (1, 3, "Three"));
        _host.UsePack(showId, 1, hash);
        return showId;
    }
}
