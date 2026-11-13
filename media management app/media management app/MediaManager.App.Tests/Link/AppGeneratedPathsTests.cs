using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.App.Tests.Link;

public sealed class AppGeneratedPathsTests
{
    // PARSE-30: importing the app's own library or Jellyfin folders must not create second records.
    [Fact]
    public void Collect_IncludesLinksAndSymlinks_ButNotImportedOriginals()
    {
        var items = new[]
        {
            new SourceItem
            {
                FilePath = @"C:\dl\a.mkv",
                LinkedPath = @"C:\lib\Shows\A\Season 01\A - S01E01.mkv",
                SymlinkPath = @"C:\jf\Shows\A\Season 01\A - S01E01.mkv"
            },
            new SourceItem
            {
                FilePath = @"C:\lib\Movies\M\M.mkv",
                LinkedPath = @"C:\lib\Movies\M\M.mkv"
            }
        };

        var generated = AppGeneratedPaths.Collect(items);

        Assert.True(AppGeneratedPaths.IsGenerated(generated, @"C:\lib\Shows\A\Season 01\A - S01E01.mkv"));
        Assert.True(AppGeneratedPaths.IsGenerated(generated, @"c:\jf\shows\a\season 01\a - s01e01.mkv"));
        Assert.False(AppGeneratedPaths.IsGenerated(generated, @"C:\lib\Movies\M\M.mkv"));
        Assert.False(AppGeneratedPaths.IsGenerated(generated, @"C:\dl\a.mkv"));
    }

    // Audit R2-A-06: a download a torrent record already links is not offered for import (it would overwrite that
    // record); an imported original is.
    [Fact]
    public void CollectLinkedDownloads_HasTorrentDownloads_ButNotImportedOriginals()
    {
        var items = new[]
        {
            new SourceItem { FilePath = @"C:\dl\a.mkv", LinkedPath = @"C:\lib\Shows\A\Season 01\A - S01E01.mkv" },
            new SourceItem { FilePath = @"C:\media\b.mkv", LinkedPath = @"C:\media\b.mkv", IsExternalImport = true },
            new SourceItem { FilePath = @"C:\dl\unlinked.mkv" }
        };

        var linked = AppGeneratedPaths.CollectLinkedDownloads(items);

        Assert.True(AppGeneratedPaths.IsGenerated(linked, @"c:\DL\a.mkv"));
        Assert.False(AppGeneratedPaths.IsGenerated(linked, @"C:\media\b.mkv"));
        Assert.False(AppGeneratedPaths.IsGenerated(linked, @"C:\dl\unlinked.mkv"));
    }
}
