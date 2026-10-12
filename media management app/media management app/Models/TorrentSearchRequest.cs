namespace media_management_app.Models;

public sealed class TorrentSearchRequest
{
    public string Query { get; init; } = string.Empty;

    public string Plugins { get; init; } = "enabled";

    public string Category { get; init; } = "all";

    public int Limit { get; init; } = 100;

    public int Offset { get; init; }

    public int IdleTimeoutSeconds { get; init; }

    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>When set, empty-engine diagnostics are logged after search completes.</summary>
    public IReadOnlyList<string>? RequestedEngineNames { get; init; }
}

