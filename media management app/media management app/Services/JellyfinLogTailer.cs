namespace media_management_app.Services;

/// <summary>
/// Append-only tail of a Jellyfin log file. Opened at EOF; poll for new lines.
/// </summary>
public sealed class JellyfinLogTailer : IDisposable
{
    private readonly AppendOnlyLogTailer _inner;

    private JellyfinLogTailer(AppendOnlyLogTailer inner)
    {
        _inner = inner;
    }

    public string FilePath => _inner.FilePath;

    public static bool TryOpen(string resolvedFilePath, out JellyfinLogTailer? tailer, out string errorMessage)
    {
        tailer = null;
        if (!AppendOnlyLogTailer.TryOpen(resolvedFilePath, out var inner, out errorMessage) || inner is null)
        {
            return false;
        }

        tailer = new JellyfinLogTailer(inner);
        return true;
    }

    public IReadOnlyList<string> ReadNewLines() => _inner.ReadNewLines();

    public static bool IsRefreshStartLine(string line) =>
        line.Contains("will be refreshed", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Lines that mean LibraryMonitor/TMDB-related activity (keeps WARP hold active).
    /// Pure Trickplay lines are ignored.
    /// </summary>
    public static bool IsActivityLine(string line)
    {
        if (IsRefreshStartLine(line))
        {
            return true;
        }

        return line.Contains("EpisodeMetadataService", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("TheMovieDb", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("SeriesMetadataService", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => _inner.Dispose();
}
