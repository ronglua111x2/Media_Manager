using System.Net;
using System.Text.Json;

namespace media_management_app.Services;

/// <summary>
/// Dual-compat helpers for qBittorrent WebAPI 5.1 (plaintext Ok./Fails.) and 5.2+ (204/401/JSON add).
/// </summary>
public static class QbittorrentWebApiCompatibility
{
    public static bool IsSuccessStatusCode(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code is >= 200 and <= 299;
    }

    public static bool IsAuthenticationFailure(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
    }

    /// <summary>
    /// Login OK: any 2xx whose body is not "Fails." (covers 200 + "Ok." and 204 empty).
    /// </summary>
    public static bool IsLoginSuccess(HttpStatusCode statusCode, string? body)
    {
        if (!IsSuccessStatusCode(statusCode))
        {
            return false;
        }

        return !string.Equals(body?.Trim(), "Fails.", StringComparison.OrdinalIgnoreCase);
    }

    public static QbittorrentAddApiResult ParseAddResponse(HttpStatusCode statusCode, string? body)
    {
        var text = (body ?? string.Empty).Trim();
        if (statusCode == HttpStatusCode.Conflict ||
            text.Equals("Fails.", StringComparison.OrdinalIgnoreCase))
        {
            return QbittorrentAddApiResult.Fail(
                string.IsNullOrWhiteSpace(text) ? $"{(int)statusCode} Conflict" : text);
        }

        if (!IsSuccessStatusCode(statusCode))
        {
            var detail = string.IsNullOrWhiteSpace(text)
                ? $"{(int)statusCode}"
                : $"{(int)statusCode} {text}";
            return QbittorrentAddApiResult.Fail(detail.Trim());
        }

        if (text.Length == 0 || text.Equals("Ok.", StringComparison.OrdinalIgnoreCase))
        {
            return QbittorrentAddApiResult.Ok();
        }

        if (text.StartsWith('{'))
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                var root = document.RootElement;
                var successCount = GetInt(root, "success_count");
                var pendingCount = GetInt(root, "pending_count");
                var failureCount = GetInt(root, "failure_count");
                var ids = GetStringArray(root, "added_torrent_ids");
                if (failureCount > 0 && successCount == 0 && pendingCount == 0)
                {
                    return QbittorrentAddApiResult.Fail(text);
                }

                return QbittorrentAddApiResult.Ok(ids);
            }
            catch (JsonException)
            {
                return QbittorrentAddApiResult.Ok();
            }
        }

        return QbittorrentAddApiResult.Ok();
    }

    private static int GetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Number)
        {
            return 0;
        }

        return property.TryGetInt32(out var value) ? value : 0;
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var ids = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var id = item.GetString();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    ids.Add(id);
                }
            }
        }

        return ids;
    }
}

public readonly record struct QbittorrentAddApiResult(bool IsSuccess, IReadOnlyList<string> AddedTorrentIds, string? ErrorDetail)
{
    public static QbittorrentAddApiResult Ok(IReadOnlyList<string>? ids = null) =>
        new(true, ids ?? [], null);

    public static QbittorrentAddApiResult Fail(string detail) =>
        new(false, [], detail);
}
