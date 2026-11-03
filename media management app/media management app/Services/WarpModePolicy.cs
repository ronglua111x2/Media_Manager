using System.Text.Json;

namespace media_management_app.Services;

/// <summary>Written before the app switches the WARP mode, so an unclean exit can restore the user's mode on next start.</summary>
public sealed record WarpModeRestoreMarker(string OriginalMode, string AppliedMode, DateTime AppliedUtc);

/// <summary>
/// WARP operating modes as used by <c>warp-cli mode</c> and <c>warp-cli -j settings</c> ("doh", "warp+doh", …).
/// The local network blocks TLS to api.themoviedb.org by SNI, so DNS-only modes cannot reach TMDB; the app
/// switches to <see cref="AppTunnelMode"/> for TMDB work and restores the user's mode afterwards.
/// </summary>
public static class WarpModePolicy
{
    public const string AppTunnelMode = "warp+doh";
    public const string MarkerFileName = "warp-mode-restore.json";

    // tunnel_only does not proxy DNS (local DNS answers TMDB with loopback); proxy is not a system tunnel.
    private static readonly HashSet<string> TunnelModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "warp",
        "warp+doh",
        "warp+dot"
    };

    private static readonly JsonSerializerOptions MarkerJsonOptions = new() { WriteIndented = true };

    /// <summary>Guards the value passed to <c>warp-cli mode</c> (it comes from the CLI's own JSON or the marker file).</summary>
    public static bool IsValidModeName(string? mode) =>
        !string.IsNullOrWhiteSpace(mode) &&
        mode.Length <= 32 &&
        mode.All(ch => ch is (>= 'a' and <= 'z') or '+' or '_');

    public static bool IsTunnelMode(string? mode) =>
        !string.IsNullOrWhiteSpace(mode) && TunnelModes.Contains(mode.Trim());

    /// <summary>Reads <c>settings.operation_mode</c> from <c>warp-cli -j settings</c>; null when missing or unparsable.</summary>
    public static string? ParseOperationMode(string settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(settingsJson);
            if (document.RootElement.TryGetProperty("settings", out var settings) &&
                settings.TryGetProperty("operation_mode", out var mode) &&
                mode.ValueKind == JsonValueKind.String)
            {
                var value = mode.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    /// <summary>
    /// Mode to restore, or null when the current mode is no longer the one the app applied
    /// (the user changed it meanwhile, so it is left alone).
    /// </summary>
    public static string? ShouldRestore(string? currentMode, WarpModeRestoreMarker marker) =>
        string.Equals(currentMode?.Trim(), marker.AppliedMode, StringComparison.OrdinalIgnoreCase)
            ? marker.OriginalMode
            : null;

    public static string SerializeMarker(WarpModeRestoreMarker marker) =>
        JsonSerializer.Serialize(marker, MarkerJsonOptions);

    public static WarpModeRestoreMarker? DeserializeMarker(string json)
    {
        try
        {
            var marker = JsonSerializer.Deserialize<WarpModeRestoreMarker>(json);
            return marker is null ||
                   string.IsNullOrWhiteSpace(marker.OriginalMode) ||
                   string.IsNullOrWhiteSpace(marker.AppliedMode)
                ? null
                : marker;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
