using System.Text.Json;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class SpecialMappingCache
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ISettingsService _settingsService;
    private readonly Dictionary<string, CachedSpecialMappingEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    public SpecialMappingCache(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public bool TryGet(string torrentHash, int tmdbId, out SpecialMappingResult? result)
    {
        EnsureLoaded();
        var key = BuildKey(torrentHash, tmdbId);
        if (_entries.TryGetValue(key, out var entry))
        {
            result = entry.Result;
            return true;
        }

        result = null;
        return false;
    }

    public void Save(string torrentHash, int tmdbId, SpecialMappingResult result)
    {
        EnsureLoaded();
        var key = BuildKey(torrentHash, tmdbId);
        _entries[key] = new CachedSpecialMappingEntry
        {
            TorrentHash = torrentHash,
            TmdbId = tmdbId,
            CachedUtc = DateTime.UtcNow,
            Result = result
        };
        Persist();
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        var path = GetCacheFilePath();
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            var entries = JsonSerializer.Deserialize<List<CachedSpecialMappingEntry>>(json) ?? [];
            foreach (var entry in entries)
            {
                _entries[BuildKey(entry.TorrentHash, entry.TmdbId)] = entry;
            }
        }
        catch
        {
            _entries.Clear();
        }
    }

    private void Persist()
    {
        try
        {
            var path = GetCacheFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(_entries.Values.ToList(), JsonOptions);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Cache persistence must not block linking.
        }
    }

    private string GetCacheFilePath() =>
        Path.Combine(_settingsService.Current.StateFolder, AppConstants.GeminiMappingCacheFileName);

    private static string BuildKey(string torrentHash, int tmdbId) =>
        $"{torrentHash.Trim().ToLowerInvariant()}|{tmdbId}";

    private sealed class CachedSpecialMappingEntry
    {
        public string TorrentHash { get; set; } = string.Empty;

        public int TmdbId { get; set; }

        public DateTime CachedUtc { get; set; }

        public SpecialMappingResult Result { get; set; } = new();
    }
}
