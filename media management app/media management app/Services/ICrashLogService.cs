using Microsoft.Web.WebView2.Core;

namespace media_management_app.Services;

public interface ICrashLogService
{
    void ReportUncleanShutdownIfNeeded();

    void MarkAlive();

    void ClearAlive();

    void LogUnhandled(string kind, string note, Exception? exception = null, bool terminating = false);

    void LogWebViewProcessFailed(string viewerName, CoreWebView2ProcessFailedEventArgs args, string? currentUrl);
}
