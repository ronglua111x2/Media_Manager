using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;
using MediaManager.App.Tests.Cleanup;
using MediaManager.App.Tests.Link;

namespace MediaManager.App.Tests.Services;

public sealed class JellyfinRefreshNowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mm-jf-now-" + Guid.NewGuid().ToString("N"));
    private readonly List<IReadOnlyList<string>> _notified = [];
    private readonly AppSettings _settings = new();
    private Exception? _notifyError;

    public JellyfinRefreshNowTests()
    {
        Directory.CreateDirectory(_root);
        _settings.AutoTrack.Jellyfin.Enabled = true;
        _settings.AutoTrack.Jellyfin.BaseUrl = "http://127.0.0.1:8096";
        _settings.AutoTrack.Jellyfin.ApiKey = "test-key";
        _settings.TmdbReadAccessToken = string.Empty; // skip the TMDB pre-probe (no network)
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private JellyfinLibraryRefreshService CreateService()
    {
        var settings = TestFake<ISettingsService>.Create((method, _) =>
            method.Name == "get_Current" ? _settings : null);
        var client = TestFake<IJellyfinClient>.Create((method, args) =>
        {
            if (method.Name != nameof(IJellyfinClient.ReportMediaUpdatedAsync))
            {
                return null;
            }

            lock (_notified)
            {
                _notified.Add(((IReadOnlyList<string>)args![0]!).ToList());
            }

            return _notifyError is null ? Task.CompletedTask : Task.FromException(_notifyError);
        });

        return new JellyfinLibraryRefreshService(
            settings,
            TestFake<IDatabaseService>.Create(),
            client,
            TestFake<IWarpCliService>.Create(),
            TestFake<IWindowsNotificationService>.Create(),
            new HttpClient(),
            new FakeAppLogger());
    }

    private string CreateLink(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, "link");
        return path;
    }

    [Fact]
    public async Task EmptyList_MakesNoHttpCall()
    {
        using var service = CreateService();

        var result = await service.RefreshNowAsync([]);

        Assert.Equal(JellyfinRefreshNowOutcome.NothingToSend, result.Outcome);
        Assert.Equal(0, result.SentCount);
        Assert.Empty(_notified);
    }

    [Fact]
    public async Task MissingPaths_AreSkippedAndCounted()
    {
        using var service = CreateService();
        var existing = CreateLink("Show 01 S01E01.mkv");
        var missing = Path.Combine(_root, "Show 01 S01E02.mkv");

        var result = await service.RefreshNowAsync([existing, missing, "  "]);

        Assert.Equal(JellyfinRefreshNowOutcome.Sent, result.Outcome);
        Assert.Equal(1, result.SentCount);
        Assert.Equal(1, result.SkippedCount);
        var sent = Assert.Single(_notified);
        Assert.Equal([Path.GetFullPath(existing)], sent);
    }

    [Fact]
    public async Task OnlyMissingPaths_MakesNoHttpCall()
    {
        using var service = CreateService();

        var result = await service.RefreshNowAsync([Path.Combine(_root, "Movie 01.mkv")]);

        Assert.Equal(JellyfinRefreshNowOutcome.NothingToSend, result.Outcome);
        Assert.Equal(1, result.SkippedCount);
        Assert.Empty(_notified);
    }

    [Fact]
    public async Task NotConfigured_ReturnsNotConfiguredWithoutThrowingOrCalling()
    {
        _settings.AutoTrack.Jellyfin.ApiKey = string.Empty;
        using var service = CreateService();

        var result = await service.RefreshNowAsync([CreateLink("Movie 01.mkv")]);

        Assert.Equal(JellyfinRefreshNowOutcome.NotConfigured, result.Outcome);
        Assert.False(service.IsConfigured);
        Assert.Empty(_notified);
    }

    [Fact]
    public async Task PendingDebounceBatch_IsMergedIntoOneNotify()
    {
        using var service = CreateService();
        var queued = CreateLink("Show 01 S01E01.mkv");
        var now = CreateLink("Show 01 S01E02.mkv");
        service.EnqueuePaths([queued]);

        var result = await service.RefreshNowAsync([now]);

        Assert.True(result.Succeeded);
        var sent = Assert.Single(_notified);
        Assert.Equal(
            new[] { Path.GetFullPath(queued), Path.GetFullPath(now) }.Order(),
            sent.Order());

        // The debounce batch was taken, so a later flush has nothing to send twice.
        await service.FlushAsync();
        Assert.Single(_notified);
    }

    [Fact]
    public async Task UnreachableServer_ReturnsFailedAndDoesNotThrow()
    {
        _notifyError = new HttpRequestException(
            HttpRequestError.ConnectionError,
            "No connection could be made because the target machine actively refused it.",
            new SocketException((int)SocketError.ConnectionRefused));
        using var service = CreateService();

        var result = await service.RefreshNowAsync([CreateLink("Movie 01.mkv")]);

        Assert.Equal(JellyfinRefreshNowOutcome.Failed, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void Targets_SelectLinkedItemsOfThatTitleOnly()
    {
        SourceItem Episode(int season, int episode, string providerId, ItemState state = ItemState.Linked) => new()
        {
            MediaKind = MediaKind.TvEpisode,
            Provider = "tmdb",
            ProviderId = providerId,
            MatchAccepted = true,
            State = state,
            SeasonNumber = season,
            EpisodeNumber = episode,
            SymlinkPath = $@"<StateFolder>\Show {providerId}\S{season:00}E{episode:00}.mkv"
        };

        var items = new[]
        {
            Episode(1, 1, "100"),
            Episode(1, 2, "100"),
            Episode(2, 1, "100"),
            Episode(2, 2, "100", ItemState.Discovered),
            Episode(1, 1, "200"),
            new SourceItem
            {
                MediaKind = MediaKind.Movie, Provider = "tmdb", ProviderId = "100", MatchAccepted = true,
                State = ItemState.Linked, SymlinkPath = @"<StateFolder>\Movie 01.mkv"
            }
        };

        Assert.Equal(3, JellyfinRefreshTargets.ForShow(100, items).Count);
        Assert.Equal(2, JellyfinRefreshTargets.ForShow(100, items, seasonNumber: 1).Count);
        Assert.Single(JellyfinRefreshTargets.ForShow(100, items, 2, 1));
        Assert.Empty(JellyfinRefreshTargets.ForShow(100, items, 2, 2));
        Assert.Single(JellyfinRefreshTargets.ForMovie(100, items));
    }
}
