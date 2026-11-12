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
