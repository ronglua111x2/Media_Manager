using System.Collections.ObjectModel;
using GeminiSpecialEval.Infrastructure;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace GeminiSpecialEval.Running;

/// <summary>Settings held in memory only: the eval never loads or saves the app's settings.json.</summary>
internal sealed class EvalSettingsService : ISettingsService
{
    public AppSettings Current { get; } = new();

    public string SettingsFilePath => string.Empty;

    public string? ActiveSettingsLogFilePath => null;

    public bool CreatedNewSettingsThisLoad => false;

    public string BootstrapSource => "gemini-eval";

    public void Load(string? bootstrapStateFolder = null)
    {
    }

    public void Save()
    {
    }
}

/// <summary>Keeps the app's log lines of the current sample (warnings and errors are worth reading in the report).</summary>
internal sealed class EvalLogger : IAppLogger
{
    private readonly List<string> _lines = [];

    public bool Echo { get; set; }

    public ObservableCollection<string> UiLogs { get; } = [];

    public ObservableCollection<AppLogEntry> UiEntries { get; } = [];

    public string? ActiveLogFilePath => null;

    public IReadOnlyList<string> Lines => _lines;

    public void Clear() => _lines.Clear();

    public void Trace(string message, LogTarget targets = LogTarget.All, string filePath = "", string memberName = "")
    {
    }

    public void Debug(string message, LogTarget targets = LogTarget.All, string filePath = "", string memberName = "")
    {
    }

    public void Info(string message, LogTarget targets = LogTarget.All, string filePath = "", string memberName = "") => Add("info", message);

    public void Warning(string message, LogTarget targets = LogTarget.All, string filePath = "", string memberName = "") => Add("warn", message);

    public void Error(string message, Exception? exception = null, LogTarget targets = LogTarget.All, string filePath = "", string memberName = "") =>
        Add("error", exception is null ? message : $"{message} ({exception.Message})");

    public void Critical(string message, Exception? exception = null, LogTarget targets = LogTarget.All, string filePath = "", string memberName = "") =>
        Add("critical", exception is null ? message : $"{message} ({exception.Message})");

    private void Add(string level, string message)
    {
        var line = $"[{level}] {SafeFile.Scrub(message)}";
        _lines.Add(line);
        if (Echo && level != "info")
        {
            Console.WriteLine("    " + line);
        }
    }
}
