using System.Text.Json;
using media_management_app.Models;

namespace media_management_app.Services.Backup;

/// <summary>
/// Produces a copy of <see cref="AppSettings"/> with known secret fields cleared, for inclusion in a backup zip.
/// No encryption is used — secrets are simply excluded; the on-disk settings.json used by the running app is
/// never touched or replaced by this type.
/// </summary>
public static class BackupSettingsRedactor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string ToRedactedJson(AppSettings source)
    {
        var clone = Clone(source);

        clone.TmdbReadAccessToken = null;
        clone.Gemini.ApiKey = null;
        clone.AutoTorrent.Username = null;
        clone.AutoTorrent.Password = null;
        clone.AutoTorrent.ApiKey = null;
        clone.AutoTrack.Jellyfin.ApiKey = null;

        return JsonSerializer.Serialize(clone, JsonOptions);
    }

    public static bool HasSecrets(AppSettings? settings)
    {
        if (settings is null)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(settings.TmdbReadAccessToken)
               || !string.IsNullOrWhiteSpace(settings.Gemini?.ApiKey)
               || !string.IsNullOrWhiteSpace(settings.AutoTorrent?.Username)
               || !string.IsNullOrWhiteSpace(settings.AutoTorrent?.Password)
               || !string.IsNullOrWhiteSpace(settings.AutoTorrent?.ApiKey)
               || !string.IsNullOrWhiteSpace(settings.AutoTrack?.Jellyfin?.ApiKey);
    }

    private static AppSettings Clone(AppSettings source)
    {
        var json = JsonSerializer.Serialize(source, JsonOptions);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
    }
}
