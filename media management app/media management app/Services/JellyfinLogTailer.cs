using System.IO;
using System.Text;

namespace media_management_app.Services;

/// <summary>
/// Append-only tail of a Jellyfin log file (FileShare.ReadWrite). Opened at EOF; poll for new lines.
/// </summary>
public sealed class JellyfinLogTailer : IDisposable
{
    private readonly FileStream _stream;
    private readonly Decoder _decoder;
    private readonly byte[] _readBuffer = new byte[8192];
    private string _pending = string.Empty;
    private bool _disposed;

    private JellyfinLogTailer(FileStream stream)
    {
        _stream = stream;
        _decoder = Encoding.UTF8.GetDecoder();
    }

    public string FilePath { get; private init; } = string.Empty;

    public static bool TryOpen(string resolvedFilePath, out JellyfinLogTailer? tailer, out string errorMessage)
    {
        tailer = null;
        errorMessage = string.Empty;

        try
        {
            var stream = new FileStream(
                resolvedFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
            stream.Seek(0, SeekOrigin.End);
            tailer = new JellyfinLogTailer(stream) { FilePath = resolvedFilePath };
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Cannot open log for tailing: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Reads newly appended text and returns complete lines (without trailing newlines).
    /// </summary>
    public IReadOnlyList<string> ReadNewLines()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var lines = new List<string>();
        while (true)
        {
            var read = _stream.Read(_readBuffer, 0, _readBuffer.Length);
            if (read <= 0)
            {
                break;
            }

            var charCount = _decoder.GetCharCount(_readBuffer, 0, read);
            var chars = new char[charCount];
            _decoder.GetChars(_readBuffer, 0, read, chars, 0);
            _pending += new string(chars);

            while (true)
            {
                var newlineIndex = _pending.IndexOfAny(['\r', '\n']);
                if (newlineIndex < 0)
                {
                    break;
                }

                var line = _pending[..newlineIndex];
                var skip = 1;
                if (_pending[newlineIndex] == '\r' &&
                    newlineIndex + 1 < _pending.Length &&
                    _pending[newlineIndex + 1] == '\n')
                {
                    skip = 2;
                }

                _pending = _pending[(newlineIndex + skip)..];
                if (line.Length > 0)
                {
                    lines.Add(line);
                }
            }
        }

        return lines;
    }

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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stream.Dispose();
    }
}
