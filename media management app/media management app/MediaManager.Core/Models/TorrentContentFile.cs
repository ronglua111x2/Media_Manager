using media_management_app.Services;

namespace media_management_app.Models;

public sealed class TorrentContentFile
{
    public string Name { get; init; } = string.Empty;

    public long Size { get; init; }

    public double Progress { get; init; }

    public int Priority { get; init; }

    public bool IsSeed { get; init; }

    public bool IsComplete => Progress >= 0.999;

    public bool IsVideoFile => TorrentFileRules.IsVideoFile(Name);
}
