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
        settings.Current.Search.NativeEnabled = true;
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

    private static SearchRecipe RecipeWith(string backend, MediaKind kind = MediaKind.Movie)
    {
        var recipe = new SearchRecipe { RecipeId = "r1", Name = "Recipe 01", TargetKind = kind };
        RecipeRuntimeSettings.SetSearchBackend(recipe, backend);
        return recipe;
    }

    private NativeSearchService CreateWith(bool nativeEnabled, bool pauseAll, out FakeSettingsService settings, FakeAppLogger? logger = null)
    {
        Directory.CreateDirectory(_stateFolder);
        settings = new FakeSettingsService();
        settings.Current.StateFolder = _stateFolder;
        settings.Current.Search.NativeEnabled = nativeEnabled;
        settings.Current.Search.PauseAll = pauseAll;
        return new NativeSearchService(settings, logger ?? new FakeAppLogger(), new TimingOutExternalIds());
    }

    private static readonly IReadOnlyList<TorrentSearchResult> QbitRows =
        [new TorrentSearchResult { FileName = "Movie.01.2013.1080p", FileUrl = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567" }];

    [Fact]
    public async Task BothMode_UsesTheQbittorrentRows_WhenNoNativeSiteCouldBeAsked()
    {
        // Every site disabled: native runs but no source answers, so the qBittorrent comparison rows are used.
        using var service = CreateWith(nativeEnabled: true, pauseAll: false, out var settings);
        foreach (var id in NativeIndexerCatalog.AllIds)
        {
            settings.Current.Search.Indexers[id] = new IndexerSettings { Enabled = false };
        }

        var rows = await service.SearchOrFallbackAsync(RecipeWith(SearchBackends.Both), "Movie 01 2013", MediaKind.Movie, () => Task.FromResult(QbitRows), CancellationToken.None);

        Assert.Single(rows);
    }

    [Fact]
    public void RecipeDecidesTheBackend_SettingsBackendIsIgnored()
    {
        using var service = CreateWith(nativeEnabled: true, pauseAll: false, out var settings);
        settings.Current.Search.Backend = SearchBackends.Native;
        settings.Current.Search.BackendByKind["Movie"] = SearchBackends.Both;

        Assert.False(service.UsesNative(MediaKind.Movie, RecipeWith(SearchBackends.Qbittorrent)));
        Assert.False(service.UsesNative(MediaKind.Movie));
        Assert.True(service.UsesNative(MediaKind.Movie, RecipeWith(SearchBackends.Native)));
        Assert.False(service.ComparesWithQbittorrent(MediaKind.Movie, RecipeWith(SearchBackends.Native)));
        Assert.True(service.ComparesWithQbittorrent(MediaKind.Movie, RecipeWith(SearchBackends.Both)));
    }

    [Theory]
    [InlineData(false, false, "turned off")]
    [InlineData(true, true, "paused")]
    public async Task NativeRecipe_WhenNativeIsOffOrPaused_SearchesWithQbittorrent_AndSaysWhy(bool nativeEnabled, bool pauseAll, string why)
    {
        var logger = new FakeAppLogger();
        using var service = CreateWith(nativeEnabled, pauseAll, out _, logger);
        var recipe = RecipeWith(SearchBackends.Native);
        var qbitCalls = 0;

        using var session = NativeSearchService.BeginSession();
        var rows = await service.SearchOrFallbackAsync(recipe, "Movie 01 2013", MediaKind.Movie, () => { qbitCalls++; return Task.FromResult(QbitRows); }, CancellationToken.None);

        Assert.Single(rows);
        Assert.Equal(1, qbitCalls);
        Assert.False(session.Session.NoSourceAvailable);
        Assert.Contains(why, session.Session.BackendNote);
        Assert.Contains("qBittorrent", session.Session.BackendNote);
        Assert.Equal("qBittorrent", ((INativeSearchService)service).BackendName(MediaKind.Movie, recipe));
        Assert.Contains(logger.Warnings, message => message.Contains(why) && message.Contains("Recipe 01"));
    }

    [Fact]
    public void FallbackWarning_IsLoggedOncePerRecipe_NotPerQuery()
    {
        var logger = new FakeAppLogger();
        using var service = CreateWith(nativeEnabled: false, pauseAll: false, out _, logger);
        var recipe = RecipeWith(SearchBackends.Both);

        for (var i = 0; i < 5; i++)
        {
            using (NativeSearchService.BeginSession())
            {
                service.UsesNative(MediaKind.Movie, recipe);
            }
        }

        Assert.Single(logger.Warnings, message => message.Contains("turned off"));
    }

    [Fact]
    public void Backend_IsChosenOncePerSession()
    {
        using var service = CreateWith(nativeEnabled: true, pauseAll: false, out var settings);
        var recipe = RecipeWith(SearchBackends.Native, MediaKind.TvEpisode);

        using (NativeSearchService.BeginSession())
        {
            Assert.True(service.UsesNative(MediaKind.TvEpisode, recipe));
            settings.Current.Search.NativeEnabled = false;
            Assert.True(service.UsesNative(MediaKind.TvEpisode, recipe));
            Assert.Equal("Native", ((INativeSearchService)service).BackendName(MediaKind.TvEpisode, recipe));
        }

        Assert.False(service.UsesNative(MediaKind.TvEpisode, recipe));
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

    [Fact]
    public void SiteBudget_DefaultsTo15SecondsInteractive_WiderInBackground_AndIsConfigurable()
    {
        var settings = new NativeSearchSettings();

        var interactive = NativeSearchService.BudgetFor(settings, SearchLane.Interactive);
        var background = NativeSearchService.BudgetFor(settings, SearchLane.Background);
        Assert.Equal(TimeSpan.FromSeconds(15), interactive.PerSite);
        Assert.Equal(TimeSpan.FromSeconds(60), background.PerSite);
        Assert.Equal(TimeSpan.FromSeconds(15), interactive.PerIndexer);
        Assert.Equal(TimeSpan.FromSeconds(90), interactive.Overall);

        settings.SiteBudgetSeconds = 25;
        settings.BackgroundSiteBudgetSeconds = 120;
        Assert.Equal(TimeSpan.FromSeconds(25), NativeSearchService.BudgetFor(settings, SearchLane.Interactive).PerSite);
        Assert.Equal(TimeSpan.FromSeconds(120), NativeSearchService.BudgetFor(settings, SearchLane.Background).PerSite);

        settings.SiteBudgetSeconds = 0;
        settings.BackgroundSiteBudgetSeconds = 99999;
        Assert.Equal(TimeSpan.FromSeconds(5), NativeSearchService.BudgetFor(settings, SearchLane.Interactive).PerSite);
        Assert.Equal(TimeSpan.FromSeconds(300), NativeSearchService.BudgetFor(settings, SearchLane.Background).PerSite);
    }

    [Fact]
    public void SitesOverBudget_ListsOnlyTheSitesCutAtTheirOwnBudget()
    {
        var runs = new[]
        {
            new IndexerRunTelemetry("kickass", "timeout", 0, 1, 15000, "15s site budget"),
            new IndexerRunTelemetry("apibay", "timeout", 0, 1, 90000, "90s search budget"),
            new IndexerRunTelemetry("knaben", "ok", 10, 1, 100, null)
        };

        Assert.Equal(["kickass"], NativeSearchService.SitesOverBudget(runs));
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
