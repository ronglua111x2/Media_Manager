using media_management_app.Models;

namespace media_management_app.Common;

/// <summary>
/// Built-in Gemini model list. Free-tier limits come from the AI Studio Rate Limit page (12 Nov 2026);
/// release and shutdown dates from https://aistudio.google.com/docs/deprecations.
/// Bump <see cref="SeedVersion"/> whenever this list changes so existing catalog files pick it up.
/// </summary>
public static class GeminiModelCatalogDefaults
{
    public const int SeedVersion = 2;

    public static GeminiModelCatalogFile CreateSeed() => new()
    {
        SeedVersion = SeedVersion,
        DefaultModel = AppConstants.DefaultGeminiModel,
        FallbackModels = ["gemini-3.1-flash-lite", "gemini-2.5-flash-lite"],
        Models =
        [
            new GeminiModelEntry
            {
                Id = "gemini-3.8-flash",
                DisplayName = "3.8 Flash",
                AutoRoute = true,
                Rank = 10,
                FreeRequestsPerMinute = 5,
                FreeRequestsPerDay = 20,
                ThinkingLevel = "low",
                Notes = "Released 2 Sep 2026. No 'minimal' thinking level."
            },
            new GeminiModelEntry
            {
                Id = "gemini-3.6-flash",
                DisplayName = "3.6 Flash",
                AutoRoute = true,
                Rank = 20,
                FreeRequestsPerMinute = 5,
                FreeRequestsPerDay = 20,
                ThinkingLevel = "minimal",
                Notes = "Released 21 Jul 2026."
            },
            new GeminiModelEntry
            {
                Id = "gemini-3.5-flash-lite",
                DisplayName = "3.5 Flash-Lite",
                AutoRoute = true,
                Rank = 30,
                FreeRequestsPerMinute = 15,
                FreeRequestsPerDay = 500,
                ThinkingLevel = "minimal",
                Notes = "Released 21 Jul 2026."
            },
            new GeminiModelEntry
            {
                Id = "gemini-3.1-flash-lite",
                DisplayName = "3.1 Flash-Lite",
                AutoRoute = true,
                Rank = 40,
                FreeRequestsPerMinute = 15,
                FreeRequestsPerDay = 500,
                ThinkingLevel = "minimal",
                RetireAfterUtc = new DateTime(2027, 5, 7, 0, 0, 0, DateTimeKind.Utc),
                Replacement = "gemini-3.5-flash-lite",
                Notes = "Released 7 May 2026; earliest shutdown 7 May 2027."
            },
            new GeminiModelEntry
            {
                Id = "gemini-2.5-flash-lite",
                DisplayName = "2.5 Flash-Lite",
                AutoRoute = true,
                Rank = 50,
                FreeRequestsPerMinute = 10,
                FreeRequestsPerDay = 20,
                Notes = "Only for accounts that used it before; no shutdown date announced."
            },
            new GeminiModelEntry
            {
                Id = "gemini-3-flash-preview",
                DisplayName = "3 Flash (preview)",
                FreeRequestsPerMinute = 5,
                FreeRequestsPerDay = 20,
                ThinkingLevel = "minimal",
                Replacement = "gemini-3.6-flash",
                Notes = "Preview model; not used by the automatic router."
            },
            new GeminiModelEntry
            {
                Id = "gemini-2.5-flash",
                DisplayName = "2.5 Flash",
                Deprecated = true,
                Replacement = "gemini-3.5-flash-lite",
                RetireAfterUtc = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc),
                Notes = "Deprecated; from 1 Dec 2026 requests route to 3.5 Flash-Lite."
            },
            new GeminiModelEntry
            {
                Id = "gemini-2.5-pro",
                DisplayName = "2.5 Pro",
                Deprecated = true,
                Replacement = "gemini-3.8-flash",
                RetireAfterUtc = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc),
                Notes = "No free-tier quota; from 1 Dec 2026 requests route to 3.8 Flash."
            },
            new GeminiModelEntry
            {
                Id = "gemini-3.1-pro-preview",
                DisplayName = "3.1 Pro (preview)",
                SupportsTextMapping = false,
                Notes = "No free-tier quota."
            },
            new GeminiModelEntry
            {
                Id = "gemini-2.0-flash",
                DisplayName = "2.0 Flash",
                Deprecated = true,
                Replacement = "gemini-3.6-flash",
                Notes = "Shut down 1 Jun 2026."
            },
            new GeminiModelEntry
            {
                Id = "gemini-2.0-flash-lite",
                DisplayName = "2.0 Flash Lite",
                Deprecated = true,
                Replacement = "gemini-3.1-flash-lite",
                Notes = "Shut down 1 Jun 2026."
            },
            new GeminiModelEntry
            {
                Id = "gemini-1.5-flash",
                DisplayName = "1.5 Flash",
                Deprecated = true,
                Replacement = "gemini-3.5-flash-lite",
                Notes = "Not available on v1beta generateContent."
            }
        ]
    };
}
