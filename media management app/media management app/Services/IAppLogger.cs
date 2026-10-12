using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using media_management_app.Common;

namespace media_management_app.Services;

public interface IAppLogger
{
    ObservableCollection<string> UiLogs { get; }

    string? ActiveLogFilePath { get; }

    void Trace(string message, LogTarget targets = LogTarget.All, [CallerFilePath] string filePath = "", [CallerMemberName] string memberName = "");

    void Debug(string message, LogTarget targets = LogTarget.All, [CallerFilePath] string filePath = "", [CallerMemberName] string memberName = "");

    void Info(string message, LogTarget targets = LogTarget.All, [CallerFilePath] string filePath = "", [CallerMemberName] string memberName = "");

    void Warning(string message, LogTarget targets = LogTarget.All, [CallerFilePath] string filePath = "", [CallerMemberName] string memberName = "");

    void Error(string message, Exception? exception = null, LogTarget targets = LogTarget.All, [CallerFilePath] string filePath = "", [CallerMemberName] string memberName = "");

    void Critical(string message, Exception? exception = null, LogTarget targets = LogTarget.All, [CallerFilePath] string filePath = "", [CallerMemberName] string memberName = "");
}
