namespace media_management_app.Models;

public sealed class GeminiModelCatalogFile
{
    public string DefaultModel { get; set; } = string.Empty;

    public List<string> FallbackModels { get; set; } = [];

    public List<GeminiModelEntry> Models { get; set; } = [];

    public DateTime? LastRefreshedUtc { get; set; }
}
