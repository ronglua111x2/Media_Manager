using System.Text;

namespace media_management_app.Services;

/// <summary>
/// Append-only tail of a log file (FileShare.ReadWrite). Opened at EOF; poll for new lines.
/// </summary>
public sealed class AppendOnlyLogTailer : IDisposable
{
    private readonly FileStream _stream;
    private readonly Decoder _decoder;
    private readonly byte[] _readBuffer = new byte[8192];
    private string _pending = string.Empty;
    private bool _disposed;

    private AppendOnlyLogTailer(FileStream stream, string filePath)
    {
        _stream = stream;
        _decoder = Encoding.UTF8.GetDecoder();
        FilePath = filePath;
    }

    public string FilePath { get; }

    public static bool TryOpen(string resolvedFilePath, out AppendOnlyLogTailer? tailer, out string errorMessage)
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
            tailer = new AppendOnlyLogTailer(stream, resolvedFilePath);
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
