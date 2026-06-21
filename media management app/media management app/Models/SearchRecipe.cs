using media_management_app.Common;

namespace media_management_app.Models;

public sealed class SearchRecipe
{
    public string RecipeId { get; set; } = Guid.NewGuid().ToString("N");

    public int Version { get; set; } = 1;

    public string MainFlowVersion { get; set; } = "recipe-flow-v1";

    public string Name { get; set; } = "Default Recipe";

    public MediaKind TargetKind { get; set; } = MediaKind.TvEpisode;

    public List<RecipeModuleConfig> Modules { get; set; } = [];
}

public sealed class RecipeModuleConfig
{
    public string ModuleId { get; set; } = Guid.NewGuid().ToString("N");

    public RecipeBlockType BlockType { get; set; }

    public int Order { get; set; }

    public int SchemaVersion { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public string DisplayName { get; set; } = string.Empty;

    public List<string> Aliases { get; set; } = [];

    public List<string> QueryTemplates { get; set; } = [];

    public List<string> CustomQueries { get; set; } = [];

    public List<string> QualityAllowList { get; set; } = ["1080p"];

    public string PreferredAudioCodec { get; set; } = string.Empty;

    public int MinimumSeeders { get; set; }

    public long? MaximumSizeBytes { get; set; }

    public List<string> IncludeTerms { get; set; } = [];

    public List<string> ExcludeTerms { get; set; } = [];

    public List<string> PreferredReleaseGroups { get; set; } = [];

    public List<string> BlockedReleaseGroups { get; set; } = [];

    public string Plugins { get; set; } = "enabled";

    public string Category { get; set; } = "all";

    public int ResultLimit { get; set; } = 100;

    public string SavePath { get; set; } = string.Empty;

    public string TorrentCategory { get; set; } = "AutoTorrent";

    public string Tags { get; set; } = string.Empty;

    public bool Paused { get; set; }

    public Dictionary<string, string> ExtensionData { get; set; } = [];
}

public enum RecipeBlockType
{
    Identity = 0,
    QueryBuilder = 1,
    SearchSource = 2,
    CandidateParser = 3,
    CandidateFilter = 4,
    Scoring = 5,
    AddTorrent = 6,
    LinkOutput = 7
}
