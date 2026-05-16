using media_management_app.Common;

namespace media_management_app.Models;

public sealed class AppLogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;

    public AppLogLevel Level { get; init; }

    public LogTarget Targets { get; init; }

    public string SourceFileName { get; init; } = string.Empty;

    public string MemberName { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string? ExceptionText { get; init; }
}
