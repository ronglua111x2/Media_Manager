using media_management_app.Common;

namespace media_management_app.Services;

public sealed class CartCandidateDebugSession
{
    private readonly int _maxLinesPerFile;
    private readonly string _logsFolder;
    private readonly string _sessionTimestamp;
    private readonly object _gate = new();
    private string _currentFilePath;
    private int _currentFileIndex;
    private int _currentLineCount;

    public CartCandidateDebugSession(string stateFolder, int maxLinesPerFile)
    {
        _maxLinesPerFile = Math.Clamp(
            maxLinesPerFile,
            AppConstants.MinLogLinesPerFile,
            AppConstants.MaxConfigurableLogLinesPerFile);
        _logsFolder = Path.Combine(stateFolder, AppConstants.LogFolderName);
        _sessionTimestamp = DateTime.Now.ToString(AppConstants.LogFileTimestampFormat);
        Directory.CreateDirectory(_logsFolder);
        _currentFilePath = BuildFilePath(0);
        FirstFilePath = _currentFilePath;
    }

    public string FirstFilePath { get; }

    public void WriteLine(string line)
    {
        lock (_gate)
        {
            if (_currentLineCount >= _maxLinesPerFile)
            {
                _currentFileIndex++;
                _currentFilePath = BuildFilePath(_currentFileIndex);
                _currentLineCount = 0;
            }

            File.AppendAllText(_currentFilePath, line + Environment.NewLine);
            _currentLineCount += CountLines(line);
        }
    }

    private string BuildFilePath(int index)
    {
        var suffix = index == 0 ? string.Empty : $"_{index}";
        return Path.Combine(_logsFolder, $"cart-debug_{_sessionTimestamp}{suffix}{AppConstants.LogFileExtension}");
    }

    private static int CountLines(string value)
    {
        if (value.Length == 0)
        {
            return 1;
        }

        return value.Count(character => character == '\n') + 1;
    }
}
