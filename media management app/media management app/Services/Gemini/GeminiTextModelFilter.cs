using media_management_app.Models;

namespace media_management_app.Services.Gemini;

public static class GeminiTextModelFilter
{
    private static readonly string[] ExcludedIdFragments =
    [
        "-tts",
        "computer-use",
        "-image",
        "live",
        "embedding",
        "aqa"
    ];

    public static bool IsSuitableForTextJsonMapping(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return false;
        }

        if (!modelId.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !ExcludedIdFragments.Any(fragment =>
            modelId.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsEligibleForFallbackPicker(GeminiModelEntry entry) =>
        !entry.Deprecated
        && entry.SupportsTextMapping
        && IsSuitableForTextJsonMapping(entry.Id);
}
