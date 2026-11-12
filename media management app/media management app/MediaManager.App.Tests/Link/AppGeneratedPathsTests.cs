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
}
