using System.IO;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Search;
using media_management_app.Services;
using MediaManager.App.Tests.Cleanup;
using Xunit;

namespace MediaManager.App.Tests.Services;

/// <summary>Audit 07c NS-03 (IMDb lookup failures never abort a search) and NS-07 (only complete answers are memoized).</summary>
public sealed class NativeSearchServiceTests : IDisposable
{
    private readonly string _stateFolder = Path.Combine(Path.GetTempPath(), "mm-native-search-tests", Guid.NewGuid().ToString("N"));

    private sealed class TimingOutExternalIds : ITmdbExternalIdService
    {
        public int Calls;

        // What HttpClient throws when its own 100 s timeout fires: a cancellation the caller did not ask for.
        public Task<string?> GetImdbIdAsync(bool isMovie, int tmdbId, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.");
        }
    }

    private NativeSearchService Create(ITmdbExternalIdService externalIds)
    {
        Directory.CreateDirectory(_stateFolder);
        var settings = new FakeSettingsService();
        settings.Current.StateFolder = _stateFolder;
        settings.Current.Search.Backend = SearchBackends.Native;
        // Kill switch: no request ever leaves the test.
        settings.Current.Search.PauseAll = true;
        return new NativeSearchService(settings, new FakeAppLogger(), externalIds);
    }

    [Fact]
    public async Task TmdbTimeoutDuringImdbLookup_DoesNotLookLikeACancel()
    {
        var externalIds = new TimingOutExternalIds();
        using var service = Create(externalIds);
        var recipe = new SearchRecipe { Name = "TV", TargetKind = MediaKind.TvEpisode };

        var rows = await service.SearchAsync(recipe, "Rick and Morty S03E05", MediaKind.TvEpisode, CancellationToken.None, new SearchSubject(false, 60625, 3, 5));

        Assert.Empty(rows);
        Assert.Equal(1, externalIds.Calls);

        // The failure is remembered for a while: the next query does not wait for TMDB again.
        await service.SearchAsync(recipe, "Rick and Morty S03E06", MediaKind.TvEpisode, CancellationToken.None, new SearchSubject(false, 60625, 3, 6));
        Assert.Equal(1, externalIds.Calls);
    }

    [Fact]
    public void Answer_IsComplete_OnlyWhenEveryAskedIndexerAnswered()
    {
        Assert.True(NativeSearchService.IsComplete(
        [
            new IndexerRunTelemetry("knaben", "ok", 10, 1, 100, null),
            new IndexerRunTelemetry("kickass", "skipped", 0, 0, 0, "disabled")
        ]));

        Assert.False(NativeSearchService.IsComplete(
        [
            new IndexerRunTelemetry("knaben", "ok", 10, 1, 100, null),
            new IndexerRunTelemetry("apibay", "timeout", 0, 1, 15000, "15s")
        ]));

        Assert.False(NativeSearchService.IsComplete(
        [
            new IndexerRunTelemetry("limetorrents", "skipped", 0, 0, 0, "breaker open until 10:00 UTC: blocked")
        ]));
    }

    [Fact]
    public void TrimmedAnswer_IsComplete()
    {
        Assert.True(NativeSearchService.IsComplete(
        [
            new IndexerRunTelemetry("bitsearch", "trimmed", 4, 1, 100, "per-search cap of 6 request(s) reached"),
            new IndexerRunTelemetry("knaben", "ok", 10, 1, 100, null)
        ]));
    }

    [Fact]
    public async Task EveryNativeSiteSkipped_IsReportedAsNoSearchSource()
    {
        using var service = Create(new TimingOutExternalIds());
        var recipe = new SearchRecipe { Name = "Movie", TargetKind = MediaKind.Movie };

        using var session = NativeSearchService.BeginSession();
        var rows = await service.SearchAsync(recipe, "Coherence 2013", MediaKind.Movie, CancellationToken.None);

        Assert.Empty(rows);
        Assert.True(session.Session.NoSourceAvailable);
        Assert.Contains("paused", session.Session.NoSourceReason);
    }

    [Fact]
    public async Task BothMode_UsesTheQbittorrentRows_WhenNoNativeSiteCouldBeAsked()
    {
        Directory.CreateDirectory(_stateFolder);
        var settings = new FakeSettingsService();
        settings.Current.StateFolder = _stateFolder;
        settings.Current.Search.Backend = SearchBackends.Both;
        settings.Current.Search.PauseAll = true;
        using var service = new NativeSearchService(settings, new FakeAppLogger(), new TimingOutExternalIds());
        var recipe = new SearchRecipe { Name = "Movie", TargetKind = MediaKind.Movie };
        IReadOnlyList<TorrentSearchResult> qbitRows = [new TorrentSearchResult { FileName = "Coherence.2013.1080p", FileUrl = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567" }];

        var rows = await service.SearchOrFallbackAsync(recipe, "Coherence 2013", MediaKind.Movie, () => Task.FromResult(qbitRows), CancellationToken.None);

        Assert.Single(rows);
    }

    [Fact]
    public void Backend_IsChosenOncePerSession()
    {
        Directory.CreateDirectory(_stateFolder);
        var settings = new FakeSettingsService();
        settings.Current.StateFolder = _stateFolder;
        settings.Current.Search.Backend = SearchBackends.Native;
        using var service = new NativeSearchService(settings, new FakeAppLogger(), new TimingOutExternalIds());

        using (NativeSearchService.BeginSession())
        {
            Assert.True(service.UsesNative(MediaKind.TvEpisode));
            settings.Current.Search.Backend = SearchBackends.Qbittorrent;
            Assert.True(service.UsesNative(MediaKind.TvEpisode));
            Assert.Equal("Native", ((INativeSearchService)service).BackendName(MediaKind.TvEpisode));
        }

        Assert.False(service.UsesNative(MediaKind.TvEpisode));
    }

    [Fact]
    public void UnreadableStateFile_StartsCautiously()
    {
        Directory.CreateDirectory(_stateFolder);
        File.WriteAllText(Path.Combine(_stateFolder, IndexerStateFile.FileName), "{ broken");
        var settings = new FakeSettingsService();
        settings.Current.StateFolder = _stateFolder;
        using var service = new NativeSearchService(settings, new FakeAppLogger(), new TimingOutExternalIds());

        var knaben = service.HealthSnapshot().Single(state => state.IndexerId == "knaben");
        Assert.Equal(700, knaben.RequestsToday);
    }

    [Fact]
    public void EztvKeepsItsAnswerLongerForBackgroundHunts()
    {
        Assert.Equal(TimeSpan.FromHours(2), NativeSearchService.CacheTtlFor("eztv", SearchLane.Background));
        Assert.Equal(TimeSpan.FromMinutes(45), NativeSearchService.CacheTtlFor("eztv", SearchLane.Interactive));
        Assert.Equal(TimeSpan.FromMinutes(45), NativeSearchService.CacheTtlFor("knaben", SearchLane.Background));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_stateFolder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
