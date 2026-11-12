namespace media_management_app.Models;

public sealed class GeminiModelCatalogFile
{
    /// <summary>
    /// Version of the built-in seed this file was last merged with. A newer seed in the app updates the known
    /// models in the file and keeps entries the seed does not know (added from ListModels or by hand).
    /// </summary>
    public int SeedVersion { get; set; }

    public string DefaultModel { get; set; } = string.Empty;

    public List<string> FallbackModels { get; set; } = [];

    public List<GeminiModelEntry> Models { get; set; } = [];

    public DateTime? LastRefreshedUtc { get; set; }
}
