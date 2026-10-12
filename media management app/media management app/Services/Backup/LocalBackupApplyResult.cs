using media_management_app.Models;

namespace media_management_app.Services.Backup;

public sealed class LocalBackupApplyResult
{
    public required string ReviewSettingsPath { get; init; }

    public AppSettings? ReviewSettings { get; init; }

    public bool SourceHasSecrets { get; init; }

    public bool DatabaseCopied { get; init; }
}
