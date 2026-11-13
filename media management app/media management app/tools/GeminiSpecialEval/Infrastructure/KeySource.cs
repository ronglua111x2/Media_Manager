using System.Text.Json;

namespace GeminiSpecialEval.Infrastructure;

/// <summary>
/// Finds the Gemini API key without printing it: an environment variable first, else (only when asked with
/// <c>--key-from-settings</c>) the <c>Gemini.ApiKey</c> of an app settings.json, opened read-only.
/// </summary>
internal static class KeySource
{
    public static readonly string[] EnvironmentVariables = ["GEMINI_EVAL_API_KEY", "GEMINI_API_KEY"];

    /// <returns>The key and where it came from (a description without the key), or null and why.</returns>
    public static (string? Key, string Origin) Resolve(string? settingsPath)
    {
        foreach (var name in EnvironmentVariables)
        {
            var value = Environment.GetEnvironmentVariable(name)?.Trim();
            if (!string.IsNullOrEmpty(value))
            {
                return (value, $"environment variable {name}");
            }
        }

        if (string.IsNullOrWhiteSpace(settingsPath))
        {
            return (null, $"no key: set {EnvironmentVariables[0]} or pass --key-from-settings <settings.json>");
        }

        // Read-only and shared: the app may have the file open, and the eval must never change it.
        using var stream = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var document = JsonDocument.Parse(stream);
        var gemini = Property(document.RootElement, "Gemini");
        var key = gemini is { ValueKind: JsonValueKind.Object } section ? Property(section, "ApiKey")?.GetString()?.Trim() : null;
        return string.IsNullOrEmpty(key)
            ? (null, $"no Gemini.ApiKey in {Path.GetFileName(settingsPath)}")
            : (key, $"Gemini.ApiKey of {settingsPath} (read-only)");
    }

    private static JsonElement? Property(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }
}
