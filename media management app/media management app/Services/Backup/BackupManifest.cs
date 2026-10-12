using media_management_app.Common;

namespace media_management_app.Services.Backup;

public sealed class BackupManifest
{
    public string AppVersion { get; set; } = string.Empty;

    public DateTime TimestampUtc { get; set; }

    public BackupTriggerType TriggerType { get; set; }

    public string MachineId { get; set; } = string.Empty;

    public string MachineName { get; set; } = string.Empty;

    public Dictionary<string, string> Checksums { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
