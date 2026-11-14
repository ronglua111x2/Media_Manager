using System.IO;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Search;
using media_management_app.Services;
using MediaManager.App.Tests.Cleanup;
using Xunit;

namespace MediaManager.App.Tests.Services;

/// <summary>
/// A show snapshot used to reach the search with a subject that had no evaluator, so the Both-mode compare log said
/// evaluated=false and the good/excellent numbers for TV and anime stayed empty (real run 13-14/11).
/// </summary>
public sealed class ShowSearchSnapshotScoringTests : IDisposable
{
    private readonly string _stateFolder = Path.Combine(Path.GetTempPath(), "mm-snapshot-scoring-tests", Guid.NewGuid().ToString("N"));

    private sealed class CapturingNativeSearch : INativeSearchService
    {
        public List<SearchSubject?> Subjects { get; } = [];

        public bool PauseAll { get; set; }

        public event Action? HealthChanged { add { } remove { } }

        public bool UsesNative(MediaKind kind, SearchRecipe? recipe = null) => true;

        public bool ComparesWithQbittorrent(MediaKind kind, SearchRecipe? recipe = null) => false;

        public Task<IReadOnlyList<TorrentSearchResult>> SearchAsync(SearchRecipe recipe, string query, MediaKind kind, CancellationToken cancellationToken, SearchSubject? subject = null) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<TorrentSearchResult>> SearchOrFallbackAsync(
            SearchRecipe recipe,
            string query,
            MediaKind kind,
            Func<Task<IReadOnlyList<TorrentSearchResult>>> qbittorrentSearch,
            CancellationToken cancellationToken,
            SearchSubject? subject = null)
        {
            Subjects.Add(subject);
            return Task.FromResult<IReadOnlyList<TorrentSearchResult>>([]);
        }

        public IReadOnlyList<IndexerStateSnapshot> HealthSnapshot() => [];

        public IndexerPolicy PolicyFor(string indexerId) => IndexerPolicy.DefaultFor(indexerId);

        public bool IsIndexerEnabled(string indexerId) => true;
    }

    private sealed class OneRecipeService : IRecipeService
    {
        public event EventHandler? RecipesChanged { add { } remove { } }

        public IReadOnlyList<SearchRecipe> GetRecipes() => [];

        public void ReloadFromDisk() { }

        public SearchRecipe GetDefaultRecipe(MediaKind targetKind) => new() { Name = "TV", TargetKind = targetKind };

        public SearchRecipe GetRecipeOrDefault(string? recipeId, MediaKind targetKind) => GetDefaultRecipe(targetKind);

        public SearchRecipe SaveRecipe(SearchRecipe recipe) => throw new NotSupportedException();

        public void PrepareRecipe(SearchRecipe recipe) { }

        public SearchRecipe DuplicateRecipe(string recipeId) => throw new NotSupportedException();

        public void DeleteRecipe(string recipeId) { }

        public SearchRecipe ImportRecipe(string filePath) => throw new NotSupportedException();

        public string ExportRecipe(string recipeId, string folderPath) => throw new NotSupportedException();

        public string GetRecipePath(string recipeId) => string.Empty;
    }

    private sealed class FixedQueries : ISearchPlanBuilder
    {
        public IReadOnlyList<string> BuildEpisodeQueries(SearchRecipe recipe, TrackedShow show, TrackedEpisode episode) => [];

        public IReadOnlyList<string> BuildMovieQueries(SearchRecipe recipe, TrackedMovie movie) => [];

        public IReadOnlyList<string> BuildShowSnapshotQueries(SearchRecipe recipe, TrackedShow show) => ["Show 01", "Show 01 1080p"];

        public IReadOnlyList<string> BuildSeasonPackQueries(SearchRecipe recipe, TrackedShow show, IReadOnlyList<int> seasons) => [];
    }

    private sealed class UnusedPlugins : IQbittorrentSearchPluginService
    {
        public Task<IReadOnlyList<SearchPluginInfo>> GetPluginsAsync(CancellationToken cancellationToken = default, bool forceRefresh = false) => throw new NotSupportedException();

        public SearchPluginResolveResult ResolveForSearch(string? savedPlugins, IReadOnlyList<SearchPluginInfo> livePlugins, IAppLogger? logger = null) => throw new NotSupportedException();

        public IReadOnlyList<string> GetRequestedEngineNames(string pluginsForApi, IReadOnlyList<SearchPluginInfo> livePlugins) => throw new NotSupportedException();

        public int ResolveEngineRankScore(string? engineName, RecipeModuleConfig? qualityModule, IAppLogger? logger = null) => 0;
    }

    private static ShowSearchSnapshotService CreateSnapshotService(CapturingNativeSearch native) => new(
        new FakeSettingsService(),
        new OneRecipeService(),
        new FixedQueries(),
        new FakeQbittorrentClient(),
        new UnusedPlugins(),
        new FakeAppLogger(),
        native);

    private static readonly TrackedShow Show = new() { Id = 1, TmdbId = 4242, Title = "Show 01" };

    [Fact]
    public async Task Snapshot_PassesItsScoringToEveryQueryOfTheSearch()
    {
        var native = new CapturingNativeSearch();
        var service = CreateSnapshotService(native);
        RecipeCandidateResult Evaluate(TorrentSearchResult row) => new() { SearchResult = row, TotalScore = 7 };

        await service.CaptureSnapshotAsync(Show, null, CancellationToken.None, scoring: new SnapshotScoring(Evaluate, 3));

        Assert.Equal(2, native.Subjects.Count);
        Assert.All(native.Subjects, subject =>
        {
            Assert.NotNull(subject);
            Assert.NotNull(subject!.Evaluate);
            Assert.Equal(3, subject.EvaluatedTargets);
            Assert.False(subject.IsMovie);
            Assert.Equal(4242, subject.TmdbId);
        });
    }

    [Fact]
    public async Task SnapshotWithoutScoring_StillSearches_WithoutAnEvaluator()
    {
        var native = new CapturingNativeSearch();
        var service = CreateSnapshotService(native);

        await service.CaptureSnapshotAsync(Show, null, CancellationToken.None);

        Assert.All(native.Subjects, subject => Assert.Null(subject!.Evaluate));
    }

    [Fact]
    public async Task BothMode_CompareRecord_IsEvaluated_WithGoodCountsAndTargets()
    {
        Directory.CreateDirectory(_stateFolder);
        var settings = new FakeSettingsService();
        settings.Current.StateFolder = _stateFolder;
        settings.Current.Search.NativeEnabled = true;
        foreach (var id in NativeIndexerCatalog.AllIds)
        {
            settings.Current.Search.Indexers[id] = new IndexerSettings { Enabled = false };
        }

        using var service = new NativeSearchService(settings, new FakeAppLogger(), new NoExternalIds());
        var recipe = new SearchRecipe { RecipeId = "r1", Name = "TV 01", TargetKind = MediaKind.TvEpisode };
        RecipeRuntimeSettings.SetSearchBackend(recipe, SearchBackends.Both);
        TorrentSearchResult[] qbit =
        [
            new() { FileName = "Show.01.S01E01.1080p.WEB", FileUrl = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567", Seeders = 40 },
            new() { FileName = "Show.01.S01E02.480p.WEB", FileUrl = "magnet:?xt=urn:btih:89abcdef0123456789abcdef0123456789abcdef", Seeders = 1 }
        ];

        await service.SearchOrFallbackAsync(
            recipe, "Show 01", MediaKind.TvEpisode, () => Task.FromResult<IReadOnlyList<TorrentSearchResult>>(qbit), CancellationToken.None,
            new SearchSubject(false, 4242, Evaluate: row => new RecipeCandidateResult { SearchResult = row, TotalScore = 5 }, EvaluatedTargets: 2));

        var line = File.ReadAllLines(Path.Combine(_stateFolder, AppConstants.LogFolderName, "search-compare.jsonl")).Single();
        using var json = System.Text.Json.JsonDocument.Parse(line);
        var root = json.RootElement;
        Assert.True(root.GetProperty("evaluated").GetBoolean());
        Assert.Equal(2, root.GetProperty("targets").GetInt32());
        Assert.Equal(1, root.GetProperty("qbittorrent").GetProperty("good").GetInt32());
    }

    private sealed class NoExternalIds : ITmdbExternalIdService
    {
        public Task<string?> GetImdbIdAsync(bool isMovie, int tmdbId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
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
