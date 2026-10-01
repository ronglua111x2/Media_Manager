using media_management_app.Services.Symlink;

namespace MediaManager.App.Tests.Cleanup;

public class SymlinkSubtitleCleanupServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly SymlinkSubtitleCleanupService _sut;
    private readonly FakeAppLogger _logger;

    public SymlinkSubtitleCleanupServiceTests()
    {
        _testDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"test_{Guid.NewGuid()}");
        System.IO.Directory.CreateDirectory(_testDir);
        _logger = new FakeAppLogger();
        _sut = new SymlinkSubtitleCleanupService(_logger);
    }

    [Fact]
    public void DeleteSubtitleFiles_WithNoSubtitles_DoesNotThrow()
    {
        var mediaFile = System.IO.Path.Combine(_testDir, "Movie.mkv");
        System.IO.File.WriteAllText(mediaFile, "dummy");

        _sut.DeleteSubtitleFiles(mediaFile);

        Assert.True(System.IO.File.Exists(mediaFile));
    }

    [Fact]
    public void DeleteSubtitleFiles_WithDirectSubtitleExtensions_DeletesAll()
    {
        var mediaFile = System.IO.Path.Combine(_testDir, "Movie.mkv");
        var srt = System.IO.Path.Combine(_testDir, "Movie.srt");
        var ass = System.IO.Path.Combine(_testDir, "Movie.ass");
        var vtt = System.IO.Path.Combine(_testDir, "Movie.vtt");

        System.IO.File.WriteAllText(mediaFile, "dummy");
        System.IO.File.WriteAllText(srt, "subtitle");
        System.IO.File.WriteAllText(ass, "subtitle");
        System.IO.File.WriteAllText(vtt, "subtitle");

        _sut.DeleteSubtitleFiles(mediaFile);

        Assert.True(System.IO.File.Exists(mediaFile));
        Assert.False(System.IO.File.Exists(srt));
        Assert.False(System.IO.File.Exists(ass));
        Assert.False(System.IO.File.Exists(vtt));
    }

    [Fact]
    public void DeleteSubtitleFiles_WithLanguageCodedSubtitles_DeletesAll()
    {
        var mediaFile = System.IO.Path.Combine(_testDir, "Episode - S01E01.mkv");
        var engSrt = System.IO.Path.Combine(_testDir, "Episode - S01E01.eng.srt");
        var fraSrt = System.IO.Path.Combine(_testDir, "Episode - S01E01.fra.srt");
        var jpnAss = System.IO.Path.Combine(_testDir, "Episode - S01E01.jpn.ass");

        System.IO.File.WriteAllText(mediaFile, "dummy");
        System.IO.File.WriteAllText(engSrt, "english subtitle");
        System.IO.File.WriteAllText(fraSrt, "french subtitle");
        System.IO.File.WriteAllText(jpnAss, "japanese subtitle");

        _sut.DeleteSubtitleFiles(mediaFile);

        Assert.True(System.IO.File.Exists(mediaFile));
        Assert.False(System.IO.File.Exists(engSrt));
        Assert.False(System.IO.File.Exists(fraSrt));
        Assert.False(System.IO.File.Exists(jpnAss));
    }

    [Fact]
    public void DeleteSubtitleFiles_WithMixedSubtitleTypes_DeletesAllFormats()
    {
        var mediaFile = System.IO.Path.Combine(_testDir, "Show - S01E01.mkv");

        var files = new[]
        {
            "Show - S01E01.srt",
            "Show - S01E01.ass",
            "Show - S01E01.ssa",
            "Show - S01E01.sub",
            "Show - S01E01.vtt",
            "Show - S01E01.sbv",
            "Show - S01E01.json",
            "Show - S01E01.eng.srt",
            "Show - S01E01.jpn.ass"
        };

        System.IO.File.WriteAllText(mediaFile, "dummy");
        foreach (var file in files)
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(_testDir, file), "subtitle");
        }

        _sut.DeleteSubtitleFiles(mediaFile);

        Assert.True(System.IO.File.Exists(mediaFile));
        foreach (var file in files)
        {
            var filePath = System.IO.Path.Combine(_testDir, file);
            Assert.False(System.IO.File.Exists(filePath), $"File {file} should have been deleted");
        }
    }

    [Fact]
    public void DeleteSubtitleFiles_WithOtherFiles_KeepsOtherFiles()
    {
        var mediaFile = System.IO.Path.Combine(_testDir, "Movie.mkv");
        var subtitle = System.IO.Path.Combine(_testDir, "Movie.srt");
        var poster = System.IO.Path.Combine(_testDir, "Movie.jpg");
        var nfo = System.IO.Path.Combine(_testDir, "Movie.nfo");

        System.IO.File.WriteAllText(mediaFile, "dummy");
        System.IO.File.WriteAllText(subtitle, "subtitle");
        System.IO.File.WriteAllText(poster, "poster");
        System.IO.File.WriteAllText(nfo, "nfo");

        _sut.DeleteSubtitleFiles(mediaFile);

        Assert.True(System.IO.File.Exists(mediaFile));
        Assert.False(System.IO.File.Exists(subtitle));
        Assert.True(System.IO.File.Exists(poster));
        Assert.True(System.IO.File.Exists(nfo));
    }

    [Fact]
    public void DeleteSubtitleFiles_WithNullPath_DoesNotThrow()
    {
        _sut.DeleteSubtitleFiles(null!);
    }

    [Fact]
    public void DeleteSubtitleFiles_WithEmptyPath_DoesNotThrow()
    {
        _sut.DeleteSubtitleFiles(string.Empty);
    }

    [Fact]
    public void DeleteSubtitleFiles_WithNonExistentPath_DoesNotThrow()
    {
        var nonExistentPath = System.IO.Path.Combine(_testDir, "nonexistent.mkv");
        _sut.DeleteSubtitleFiles(nonExistentPath);
    }

    [Fact]
    public void DeleteSubtitleFiles_WithDifferentFileNames_IgnoresSubtitles()
    {
        var mediaFile = System.IO.Path.Combine(_testDir, "Movie.mkv");
        var otherSubtitle = System.IO.Path.Combine(_testDir, "OtherMovie.srt");

        System.IO.File.WriteAllText(mediaFile, "dummy");
        System.IO.File.WriteAllText(otherSubtitle, "subtitle");

        _sut.DeleteSubtitleFiles(mediaFile);

        Assert.True(System.IO.File.Exists(mediaFile));
        Assert.True(System.IO.File.Exists(otherSubtitle));
    }

    [Fact]
    public void DeleteSubtitleFiles_CaseInsensitive_DeletesSubtitles()
    {
        var mediaFile = System.IO.Path.Combine(_testDir, "movie.mkv");
        var srtUpper = System.IO.Path.Combine(_testDir, "movie.SRT");
        var srtMixed = System.IO.Path.Combine(_testDir, "movie.Srt");

        System.IO.File.WriteAllText(mediaFile, "dummy");
        System.IO.File.WriteAllText(srtUpper, "subtitle");
        System.IO.File.WriteAllText(srtMixed, "subtitle");

        _sut.DeleteSubtitleFiles(mediaFile);

        Assert.True(System.IO.File.Exists(mediaFile));
        Assert.False(System.IO.File.Exists(srtUpper));
        Assert.False(System.IO.File.Exists(srtMixed));
    }

    public void Dispose()
    {
        try
        {
            if (System.IO.Directory.Exists(_testDir))
            {
                System.IO.Directory.Delete(_testDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors in test
        }
    }
}
