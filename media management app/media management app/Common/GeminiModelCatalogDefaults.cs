using media_management_app.Models;

namespace media_management_app.Common;

public static class GeminiModelCatalogDefaults
{
    public static GeminiModelCatalogFile CreateSeed() => new()
    {
        DefaultModel = AppConstants.DefaultGeminiModel,
        FallbackModels = ["gemini-2.0-flash", "gemini-2.5-flash"],
        Models =
        [
            new GeminiModelEntry
            {
                Id = "gemini-2.5-flash-lite",
                DisplayName = "2.5 Flash Lite",
                Deprecated = false,
                SupportsTextMapping = true,
                Notes = "Default; best RPM on free tier"
            },
            new GeminiModelEntry
            {
                Id = "gemini-2.0-flash",
                DisplayName = "2.0 Flash",
                Deprecated = false,
                SupportsTextMapping = true
            },
            new GeminiModelEntry
            {
                Id = "gemini-2.5-flash",
                DisplayName = "2.5 Flash",
                Deprecated = false,
                SupportsTextMapping = true
            },
            new GeminiModelEntry
            {
                Id = "gemini-2.5-pro",
                DisplayName = "2.5 Pro",
                Deprecated = false,
                SupportsTextMapping = true
            },
            new GeminiModelEntry
            {
                Id = "gemini-2.0-flash-lite",
                DisplayName = "2.0 Flash Lite",
                Deprecated = true,
                Replacement = "gemini-2.5-flash-lite",
                Notes = "Shut down June 2026"
            },
            new GeminiModelEntry
            {
                Id = "gemini-1.5-flash",
                DisplayName = "1.5 Flash",
                Deprecated = true,
                Replacement = "gemini-2.5-flash-lite",
                Notes = "Not available on v1beta generateContent"
            }
        ]
    };
}
