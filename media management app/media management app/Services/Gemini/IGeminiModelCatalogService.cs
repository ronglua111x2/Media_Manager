using media_management_app.Models;

namespace media_management_app.Services.Gemini;

public interface IGeminiModelCatalogService
{
    GeminiModelCatalogFile Current { get; }

    string CatalogFilePath { get; }

    string DefaultModel { get; }

    IReadOnlyList<string> FallbackModels { get; }

    IReadOnlyList<GeminiModelEntry> GetModelsForUi();

    IReadOnlyList<GeminiModelEntry> GetTextMappingModelsForUi();

    string Normalize(string? modelId);

    void ReloadFromDisk();

    Task<GeminiModelRefreshResult> RefreshFromApiAsync(CancellationToken cancellationToken = default);
}
