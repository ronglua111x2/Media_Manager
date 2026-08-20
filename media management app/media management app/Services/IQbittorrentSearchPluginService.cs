using media_management_app.Models;

namespace media_management_app.Services;

public interface IQbittorrentSearchPluginService
{
    Task<IReadOnlyList<SearchPluginInfo>> GetPluginsAsync(CancellationToken cancellationToken = default, bool forceRefresh = false);

    SearchPluginResolveResult ResolveForSearch(string? savedPlugins, IReadOnlyList<SearchPluginInfo> livePlugins, IAppLogger? logger = null);

    IReadOnlyList<string> GetRequestedEngineNames(string pluginsForApi, IReadOnlyList<SearchPluginInfo> livePlugins);

    int ResolveEngineRankScore(string? engineName, RecipeModuleConfig? qualityModule, IAppLogger? logger = null);
}
