using Microsoft.Web.WebView2.Core;

namespace media_management_app.Services;

public interface ICrashLogService
{
    void ReportUncleanShutdownIfNeeded();

    void MarkAlive();

    void ClearAlive();

    void LogUnhandled(string kind, string note, Exception? exception = null, bool terminating = false);

    void LogLifetime(string kind, string note, int? exitCode = null);

    /// <summary>
    /// Appends one line to this session's lifetime trace file and returns after the write.
    /// Use for shutdown paths where the queued app log may be lost when the process is torn down.
    /// </summary>
    void TraceLifetime(string message);

    void LogWebViewProcessFailed(string viewerName, CoreWebView2ProcessFailedEventArgs args, string? currentUrl);
}
