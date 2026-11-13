using System.Net.Http;
using GeminiSpecialEval.Cases;
using GeminiSpecialEval.Prompts;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;
using media_management_app.Services.Gemini;
using media_management_app.Services.Gemini.Routing;

namespace GeminiSpecialEval.Running;

/// <summary>What the app made of one case: the final mapping and what went over the wire.</summary>
internal sealed record CaseRun(SpecialMappingResult Result, VariantGeminiClient Client, IReadOnlyList<string> LogLines);

/// <summary>
/// The app's own Gemini stack (catalog, router, availability store, <see cref="GeminiApiClient"/>), pinned to one
/// model, over an HTTP handler the eval controls: the live guard, a replay of recorded responses, or a responder.
/// Each case runs through the app's real <see cref="SpecialMappingOrchestrator"/> and
/// <see cref="GeminiSpecialMappingProvider"/>, so payload, parsing and every hard check are the app's.
/// </summary>
internal sealed class GeminiStack : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string? _temporaryFolder;

    private GeminiStack(string stateFolder, string modelLabel, string apiKey, HttpMessageHandler handler, bool temporary)
    {
        var spec = ModelSpec.Parse(modelLabel);
        var model = spec.Id;
        Model = spec.Label;
        _temporaryFolder = temporary ? stateFolder : null;
        Directory.CreateDirectory(stateFolder);
        Settings.Current.StateFolder = stateFolder;
        Settings.Current.Gemini = new GeminiSettings
        {
            Enabled = true,
            ApiKey = apiKey,
            AutoSelectModel = false,
            Model = model,
            // The same model as its own only fallback: the router's chain is exactly [model].
            FallbackModels = [model],
            TimeoutSeconds = AppConstants.GeminiDefaultTimeoutSeconds
        };

        _httpClient = new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(200) };
        var catalog = new GeminiModelCatalogService(Settings, _httpClient, Logger);
        if (spec.ThinkingLevel is not null)
        {
            // In memory only: the router reads the entry on every request, and the catalog file is not saved.
            var entry = catalog.Current.Models.FirstOrDefault(item => string.Equals(item.Id, model, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException($"'{model}' is not in the catalog; a thinking override needs a catalog entry.");
            entry.ThinkingLevel = spec.ThinkingLevel;
            entry.ThinkingBudget = null;
        }

        Router = new GeminiModelRouter(Settings, catalog, new GeminiModelAvailabilityStore(Settings, TimeProvider.System), Logger);
        var chain = Router.GetCandidateModels();
        if (chain.Count != 1 || !string.Equals(chain[0], model, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The router would try [{string.Join(", ", chain)}] instead of only '{model}'. Use the model's catalog id.");
        }

        Client = new GeminiApiClient(Settings, _httpClient, Router, Logger);
    }

    /// <summary>The model label (catalog id, plus "@level" when the thinking level is overridden).</summary>
    public string Model { get; }

    public EvalSettingsService Settings { get; } = new();

    public EvalLogger Logger { get; } = new();

    public GeminiModelRouter Router { get; }

    public GeminiApiClient Client { get; }

    /// <summary>A stack that sends real requests through <paramref name="guard"/>; its router state persists in <paramref name="stateFolder"/>.</summary>
    public static GeminiStack Live(string stateFolder, string model, string apiKey, Recording.LiveGuardHandler guard) =>
        new(stateFolder, model, apiKey, guard, temporary: false);

    /// <summary>A stack that never reaches the network: fresh router state in a temporary folder, a dummy key.</summary>
    public static GeminiStack Offline(string model, HttpMessageHandler handler) =>
        new(Path.Combine(Path.GetTempPath(), $"gemini-eval-{Guid.NewGuid():N}"), model, "offline-replay-key", handler, temporary: true);

    public async Task<CaseRun> RunCaseAsync(EvalCase evalCase, PromptVariant variant, CancellationToken cancellationToken)
    {
        Logger.Clear();
        var client = new VariantGeminiClient(Client, variant);
        var orchestrator = new SpecialMappingOrchestrator(
            Settings,
            new GeminiSpecialMappingProvider(client, Settings, Logger),
            new SpecialMappingCache(Settings),
            Logger);
        var tree = PackFolderTreeAnalyzer.Analyze(evalCase.Files.Select(file => file.RelativePath).ToList());
        var result = await orchestrator.ResolveAsync(
            evalCase.Files,
            tree,
            evalCase.Episodes,
            evalCase.ShowTitle,
            evalCase.TmdbId,
            "eval-" + evalCase.Name,
            progress: null,
            useGemini: true,
            bypassCache: true,
            cancellationToken: cancellationToken);
        return new CaseRun(result, client, Logger.Lines.ToList());
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        if (_temporaryFolder is not null)
        {
            try
            {
                Directory.Delete(_temporaryFolder, recursive: true);
            }
            catch
            {
                // Best effort.
            }
        }
    }
}
