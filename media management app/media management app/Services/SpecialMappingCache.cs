using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

/// <summary>
/// Special/OVA mappings the user accepted, so the same pack does not cost another Gemini request (PR-15).
/// </summary>
/// <remarks>
/// Key v2: torrent hash, TMDB id, a hash of the TMDB Season 0 list, a hash of the pack's special files and the
/// prompt version. A change on any side (TMDB adds a special, a different file list, a new prompt) is a miss.
/// Entries written before v2 (key: hash and TMDB id only) are kept; the orchestrator re-checks them before use.
/// Only the fields linking needs are stored (season, episode, title, air date), not overviews or notes.
/// </remarks>
public sealed class SpecialMappingCache
{
    private const int CurrentKeyVersion = 2;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ISettingsService _settingsService;
    private readonly object _sync = new();
    private readonly Dictionary<string, CachedSpecialMappingEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    public SpecialMappingCache(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public static string BuildKey(
        string torrentHash,
        int tmdbId,
        IReadOnlyList<TrackedEpisode> specialsEpisodes,
        IReadOnlyList<SpecialMappingCandidate> candidates,
        int promptVersion)
    {
        var specials = string.Join('\n', specialsEpisodes
            .Where(episode => episode.SeasonNumber == AppConstants.SpecialsSeasonNumber)
            .OrderBy(episode => episode.EpisodeNumber)
            .Select(episode => $"{episode.EpisodeNumber}|{episode.Title}|{episode.AirDateDisplay}"));
        var files = string.Join('\n', candidates
            .Select(candidate => candidate.RelativePath.Replace('\\', '/'))
            .Order(StringComparer.OrdinalIgnoreCase));
        return $"v{CurrentKeyVersion}|{NormalizeHash(torrentHash)}|{tmdbId}|{Fingerprint(specials)}|{Fingerprint(files)}|p{promptVersion}";
    }

    /// <summary>
    /// Looks up <paramref name="key"/>; without it, a pre-v2 entry for the same torrent and show.
    /// <paramref name="isLegacy"/> tells the caller to check the legacy entry against the rules before using it.
    /// </summary>
    public bool TryGet(string key, string torrentHash, int tmdbId, out CachedSpecialMapping? cached, out bool isLegacy)
    {
        lock (_sync)
        {
            EnsureLoaded();
            isLegacy = false;
            if (_entries.TryGetValue(key, out var entry))
            {
                cached = new CachedSpecialMapping(entry.Result, entry.Model);
                return true;
            }

            if (_entries.TryGetValue(LegacyKey(torrentHash, tmdbId), out entry))
            {
                isLegacy = true;
                cached = new CachedSpecialMapping(entry.Result, entry.Model);
                return true;
            }

            cached = null;
            return false;
        }
    }

    /// <summary>Forgets the entry for <paramref name="key"/> and any pre-v2 entry for the same torrent and show.</summary>
    public void Remove(string key, string torrentHash, int tmdbId)
    {
        lock (_sync)
        {
            EnsureLoaded();
            var removed = _entries.Remove(key) | _entries.Remove(LegacyKey(torrentHash, tmdbId));
            if (removed)
            {
                Persist();
            }
        }
    }

    /// <summary>Stores an accepted mapping. A result with candidates but no mapped file is not stored.</summary>
    public void Save(string key, string torrentHash, int tmdbId, SpecialMappingResult result, string? model)
    {
        if (result.Items.Count > 0 && result.Items.All(item => item.Episode is null))
        {
            return;
        }

        lock (_sync)
        {
            EnsureLoaded();
            _entries[key] = new CachedSpecialMappingEntry
            {
                KeyVersion = CurrentKeyVersion,
                Key = key,
                TorrentHash = torrentHash,
                TmdbId = tmdbId,
                CachedUtc = DateTime.UtcNow,
                Model = model,
                Result = Slim(result)
            };
            Persist();
        }
    }

    private static SpecialMappingResult Slim(SpecialMappingResult result)
    {
        var items = result.Items
            .Select(item => new SpecialMappingItem
            {
                RelativePath = item.RelativePath,
                Episode = SlimEpisode(item.Episode),
                Source = item.Source,
                ProposalReason = item.ProposalReason,
                Reason = item.Reason,
                TitleConfirmed = item.TitleConfirmed,
                SuggestedEpisode = SlimEpisode(item.SuggestedEpisode),
                MatchesSuggestion = item.MatchesSuggestion
            })
            .ToList();
        return new SpecialMappingResult
        {
            Items = items,
            Warnings = result.Warnings,
            UsedGemini = result.UsedGemini,
            ByPath = items.ToDictionary(item => item.RelativePath, item => item, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static TrackedEpisode? SlimEpisode(TrackedEpisode? episode) =>
        episode is null
            ? null
            : new TrackedEpisode
            {
                SeasonNumber = episode.SeasonNumber,
                EpisodeNumber = episode.EpisodeNumber,
                Title = episode.Title,
                AirDate = episode.AirDate
            };

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
            var entries = JsonSerializer.Deserialize<List<CachedSpecialMappingEntry>>(json, JsonOptions) ?? [];
            foreach (var entry in entries)
            {
                var key = entry.KeyVersion >= CurrentKeyVersion && !string.IsNullOrWhiteSpace(entry.Key)
                    ? entry.Key
                    : LegacyKey(entry.TorrentHash, entry.TmdbId);
                _entries[key] = entry;
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

    private static string LegacyKey(string torrentHash, int tmdbId) =>
        $"{NormalizeHash(torrentHash)}|{tmdbId}";

    private static string NormalizeHash(string torrentHash) => torrentHash.Trim().ToLowerInvariant();

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16].ToLowerInvariant();

    private sealed class CachedSpecialMappingEntry
    {
        /// <summary>0 for entries written before PR-15 (key: torrent hash and TMDB id only).</summary>
        public int KeyVersion { get; set; }

        public string? Key { get; set; }

        public string TorrentHash { get; set; } = string.Empty;

        public int TmdbId { get; set; }

        public DateTime CachedUtc { get; set; }

        /// <summary>The Gemini model that answered (null for entries written before PR-15).</summary>
        public string? Model { get; set; }

        public SpecialMappingResult Result { get; set; } = new();
    }
}

public sealed record CachedSpecialMapping(SpecialMappingResult Result, string? Model);
