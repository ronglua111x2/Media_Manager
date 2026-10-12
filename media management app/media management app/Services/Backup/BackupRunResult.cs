using media_management_app.Common;

namespace media_management_app.Services.Backup;

public sealed class BackupRunResult
{
    public bool Succeeded { get; init; }

    public string Summary { get; init; } = string.Empty;

    public BackupTriggerType TriggerType { get; init; }

    public static BackupRunResult Success(BackupTriggerType triggerType, string summary) =>
        new() { Succeeded = true, TriggerType = triggerType, Summary = summary };

    public static BackupRunResult Failure(BackupTriggerType triggerType, string summary) =>
        new() { Succeeded = false, TriggerType = triggerType, Summary = summary };
}
