using System.IO;
using System.Reflection;
using System.Text.Json;
using MediaManager.App.Tests.Cleanup;
using MediaManager.App.Tests.Special.Fixtures;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;
using media_management_app.Services.Events;
using media_management_app.Services.Gemini;

namespace MediaManager.App.Tests.Special;

/// <summary>Interface fake built on <see cref="DispatchProxy"/>: unhandled calls return default values.</summary>
public class SpecialProxyFake<T> : DispatchProxy where T : class
{
    private Func<MethodInfo, object?[]?, object?>? _handler;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var handled = _handler?.Invoke(targetMethod!, args);
        if (handled is not null)
        {
            return handled;
        }

        var returnType = targetMethod!.ReturnType;
        if (returnType == typeof(void))
        {
            return null;
        }

        if (returnType == typeof(Task))
        {
            return Task.CompletedTask;
        }

        return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
    }

    public static T Create(Func<MethodInfo, object?[]?, object?>? handler = null)
    {
        var proxy = Create<T, SpecialProxyFake<T>>();
        ((SpecialProxyFake<T>)(object)proxy)._handler = handler;
        return proxy;
    }
}

/// <summary>
/// Gemini client that never touches the network. Each call runs <see cref="Respond"/> with the
/// serialized request; it returns the response text or throws, like the real client does.
/// </summary>
internal sealed class ScriptedGeminiClient : IGeminiApiClient
{
    public ScriptedGeminiClient(Func<string, string> respond)
    {
        Respond = respond;
    }

    public Func<string, string> Respond { get; }

    public string? FinishReason { get; init; }

    public int Calls { get; private set; }

    public Task<string> TestConnectionAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<string> GenerateJsonTextAsync(string systemPrompt, string userJson, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<GeminiJsonTextResult> GenerateJsonTextWithModelAsync(
        string systemPrompt,
        string userJson,
        JsonElement? responseSchema = null,
        IProgress<PackLinkProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new GeminiJsonTextResult("fake-model", Respond(userJson), FinishReason));
    }

    public Task<T> GenerateJsonAsync<T>(string systemPrompt, string userJson, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

/// <summary>Real orchestrator, provider and cache on a temporary state folder; only the Gemini HTTP client is fake.</summary>
internal sealed class SpecialOrchestratorHost : IDisposable
{
    public SpecialOrchestratorHost(Func<string, string> respond, bool geminiEnabled = true, string? finishReason = null)
    {
        StateFolder = Path.Combine(Path.GetTempPath(), $"mm-special-{Guid.NewGuid():N}");
        Directory.CreateDirectory(StateFolder);
        Settings.Current.StateFolder = StateFolder;
        Settings.Current.Gemini = new GeminiSettings { Enabled = geminiEnabled, ApiKey = geminiEnabled ? "test-key" : null };
        Gemini = new ScriptedGeminiClient(respond) { FinishReason = finishReason };
        Orchestrator = new SpecialMappingOrchestrator(
            Settings,
            new GeminiSpecialMappingProvider(Gemini, Settings, Logger),
            new SpecialMappingCache(Settings),
            Logger);
    }

    public string StateFolder { get; }

    public FakeSettingsService Settings { get; } = new();

    public FakeAppLogger Logger { get; } = new();

    public ScriptedGeminiClient Gemini { get; }

    public SpecialMappingOrchestrator Orchestrator { get; }

    public Task<SpecialMappingResult> ResolveAsync(SpecialCase pack, bool useGemini = true, CancellationToken cancellationToken = default)
    {
        var tree = PackFolderTreeAnalyzer.Analyze(pack.Files.Select(file => file.RelativePath).ToList());
        return Orchestrator.ResolveAsync(
            pack.Files,
            tree,
            pack.Specials,
            pack.ShowTitle,
            pack.TmdbId,
            "hash-" + pack.Name,
            progress: null,
            useGemini: useGemini,
            bypassCache: true,
            cancellationToken: cancellationToken);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(StateFolder, recursive: true);
        }
        catch
        {
            // Temporary folder cleanup is best effort.
        }
    }
}

/// <summary>
/// <see cref="AutoTorrentLinkService"/> with the database and qBittorrent faked through proxies, enough to run
/// <c>PrepareSeasonPackLinkAsync</c> (it only reads the show, seasons, episodes and the torrent file list).
/// </summary>
internal static class SpecialPackPrepareHost
{
    public const long ShowId = 1;
    public const string Hash = "packhash";

    public static AutoTorrentLinkService Create(SpecialCase pack, ISpecialMappingOrchestrator orchestrator, int ownerSeason = 1)
    {
        var show = new TrackedShow { Id = ShowId, TmdbId = pack.TmdbId, Title = pack.ShowTitle };
        var seasons = pack.Seasons
            .Select(season => new TrackedSeason
            {
                ShowId = ShowId,
                SeasonNumber = season.SeasonNumber,
                EpisodeCount = season.EpisodeCount,
                ManagementMode = SeasonManagementMode.Pack,
                PackTorrentHash = season.SeasonNumber == ownerSeason ? Hash : null,
                SelectedPackOwnerSeasonNumber = ownerSeason,
                SelectedPackCoveredSeasons = string.Join(",", pack.Seasons.Select(item => item.SeasonNumber))
            })
            .ToList();

        var database = SpecialProxyFake<IDatabaseService>.Create((method, _) => method.Name switch
        {
            "GetTrackedShow" => (object)show,
            "GetTrackedSeasons" => seasons,
            "GetTrackedEpisodes" => pack.Episodes.ToList(),
            _ => null
        });

        var torrent = new AddedTorrentResult { Hash = Hash, Name = pack.Name, State = "uploading", Progress = 1, SavePath = @"X:\downloads" };
        var files = pack.Files
            .Select(file => new TorrentContentFile { Name = file.RelativePath, Size = 1_000_000, Progress = 1 })
            .ToList();
        var qbittorrent = SpecialProxyFake<IQbittorrentClient>.Create((method, _) => method.Name switch
        {
            "GetTorrentsAsync" => (object)Task.FromResult<IReadOnlyList<AddedTorrentResult>>([torrent]),
            "GetTorrentFilesAsync" => Task.FromResult<IReadOnlyList<TorrentContentFile>>(files),
            _ => null
        });

        return new AutoTorrentLinkService(
            database,
            qbittorrent,
            SpecialProxyFake<IHardlinkService>.Create(),
            SpecialProxyFake<ILibraryLinkEventHub>.Create(),
            orchestrator,
            new FakeAppLogger());
    }

    /// <summary>An orchestrator whose every call throws, like an unhandled Gemini or payload failure.</summary>
    public static ISpecialMappingOrchestrator ThrowingOrchestrator(Exception error) =>
        SpecialProxyFake<ISpecialMappingOrchestrator>.Create((method, _) =>
            method.Name == nameof(ISpecialMappingOrchestrator.ResolveAsync)
                ? Task.FromException<SpecialMappingResult>(error)
                : null);
}
