using System.Text.Json;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

/// <summary>
/// Reads the 200 answer of qBittorrent's <c>torrents/fetchMetadata</c> (WebAPI ≥ 2.11.9):
/// <c>{"hash": "...", "infohash_v1": "...", "info": {"name": "...", "files": [{"path": "...", "length": n}, ...]}}</c>.
/// The 202 answer (still fetching) has no <c>info</c> and yields null.
/// </summary>
public static class QbittorrentFetchedMetadata
{
    public static PreAddInspection? Parse(string magnet, string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("info", out var info) ||
                info.ValueKind != JsonValueKind.Object ||
                !info.TryGetProperty("files", out var filesElement) ||
                filesElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var files = new List<TorrentContentFile>();
            foreach (var file in filesElement.EnumerateArray())
            {
                files.Add(new TorrentContentFile
                {
                    Name = file.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String ? path.GetString() ?? string.Empty : string.Empty,
                    Size = file.TryGetProperty("length", out var length) && length.TryGetInt64(out var size) ? size : 0
                });
            }

            if (files.Count == 0)
            {
                return null;
            }

            var hash = ReadString(root, "infohash_v1") ?? ReadString(root, "hash");
            return new PreAddInspection
            {
                AddUrl = magnet,
                InfoHash = TorrentListingIdentity.NormalizeInfoHash(hash) ?? TorrentListingIdentity.TryParseInfoHashFromUrl(magnet),
                Name = ReadString(info, "name") ?? string.Empty,
                Files = files,
                MetadataPrefetched = true
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
