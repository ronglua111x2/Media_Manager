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
