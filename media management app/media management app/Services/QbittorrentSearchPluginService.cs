using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class QbittorrentSearchPluginService : IQbittorrentSearchPluginService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(45);

    private readonly IQbittorrentClient _qbittorrentClient;
    private readonly object _gate = new();
    private IReadOnlyList<SearchPluginInfo>? _cachedPlugins;
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;

    public QbittorrentSearchPluginService(IQbittorrentClient qbittorrentClient)
    {
        _qbittorrentClient = qbittorrentClient;
    }

    public async Task<IReadOnlyList<SearchPluginInfo>> GetPluginsAsync(
        CancellationToken cancellationToken = default,
        bool forceRefresh = false)
    {
        lock (_gate)
        {
            if (!forceRefresh &&
                _cachedPlugins is not null &&
                DateTimeOffset.UtcNow - _cachedAt < CacheLifetime)
            {
                return _cachedPlugins;
            }
        }

        var plugins = await _qbittorrentClient.GetSearchPluginsAsync(cancellationToken);
        lock (_gate)
        {
            _cachedPlugins = plugins;
            _cachedAt = DateTimeOffset.UtcNow;
        }

        return plugins;
    }

    public SearchPluginResolveResult ResolveForSearch(
        string? savedPlugins,
        IReadOnlyList<SearchPluginInfo> livePlugins,
        IAppLogger? logger = null)
    {
        var normalized = string.IsNullOrWhiteSpace(savedPlugins) ? "enabled" : savedPlugins.Trim();
        if (normalized.Equals("enabled", StringComparison.OrdinalIgnoreCase))
        {
            var enabledNames = livePlugins
                .Where(plugin => plugin.Enabled)
                .Select(plugin => plugin.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new SearchPluginResolveResult
            {
                PluginsForApi = "enabled",
                RequestedNames = enabledNames
            };
        }

        var requested = normalized
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (requested.Count == 0)
        {
            return new SearchPluginResolveResult
            {
                PluginsForApi = "enabled",
                RequestedNames = livePlugins.Where(plugin => plugin.Enabled).Select(plugin => plugin.Name).ToList()
            };
        }

        var liveByName = livePlugins.ToDictionary(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase);
        var survivors = new List<string>();
        var skipped = new List<string>();
        foreach (var name in requested)
        {
            if (!liveByName.TryGetValue(name, out var plugin))
            {
                skipped.Add(name);
                logger?.Warning($"Search plugin '{name}' is not installed and will be skipped.", LogTarget.All);
                continue;
            }

            if (!plugin.Enabled)
            {
                skipped.Add(name);
                logger?.Warning($"Search plugin '{name}' is disabled in qBittorrent and will be skipped.", LogTarget.All);
                continue;
            }

            survivors.Add(plugin.Name);
        }

        if (survivors.Count == 0)
        {
            logger?.Warning(
                $"No valid enabled search plugins remain after resolving recipe selection ({string.Join(", ", requested)}). Search will be skipped.",
                LogTarget.All);
            return new SearchPluginResolveResult
            {
                PluginsForApi = string.Empty,
                RequestedNames = requested,
                SkipSearch = true,
                SkippedNames = skipped
            };
        }

        return new SearchPluginResolveResult
        {
            PluginsForApi = string.Join("|", survivors),
            RequestedNames = survivors,
            SkippedNames = skipped
        };
    }

    public IReadOnlyList<string> GetRequestedEngineNames(string pluginsForApi, IReadOnlyList<SearchPluginInfo> livePlugins)
    {
        if (string.IsNullOrWhiteSpace(pluginsForApi) ||
            pluginsForApi.Equals("enabled", StringComparison.OrdinalIgnoreCase))
        {
            return livePlugins
                .Where(plugin => plugin.Enabled)
                .Select(plugin => plugin.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return pluginsForApi
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public int ResolveEngineRankScore(string? engineName, RecipeModuleConfig? qualityModule, IAppLogger? logger = null)
    {
        var score = RecipeRuntimeSettings.ResolveEngineRankScore(engineName, qualityModule);
        if (score == 0 &&
            qualityModule is not null &&
            !string.IsNullOrWhiteSpace(engineName) &&
            RecipeRuntimeSettings.GetEnginePriority(qualityModule).Names.Count > 0 &&
            !RecipeRuntimeSettings.GetEnginePriority(qualityModule).Names.Contains(engineName, StringComparer.OrdinalIgnoreCase))
        {
            return 0;
        }

        return score;
    }
}
