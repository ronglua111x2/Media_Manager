namespace media_management_app.Models;

public sealed class BackupSettings
{
    /// <summary>Stable per-install identity, generated once. Not <see cref="Environment.MachineName"/> because that can change.</summary>
    public string MachineId { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    /// <summary>User-chosen path to the Google OAuth client JSON (e.g. the "client_secret_....json" downloaded from Google Cloud Console). Falls back to a default path under the state folder when empty.</summary>
    public string? CredentialsFilePath { get; set; }

    /// <summary>Hour of day (0-23, local time) the daily safety-net backup runs.</summary>
    public int DailyBackupHour { get; set; } = 3;

    /// <summary>Debounce window for settings.json/Recipes changes before an event-driven backup runs.</summary>
    public int EventDebounceMinutes { get; set; } = 20;

    /// <summary>Minimum time between database backups, since media-manager.db is written continuously.</summary>
    public int DbThrottleHours { get; set; } = 1;

    /// <summary>How many zips to keep under history/, regardless of which trigger produced them.</summary>
    public int HistoryRetentionCount { get; set; } = 20;

    public string? DriveRootFolderId { get; set; }

    public string? DriveMachineFolderId { get; set; }

    public string? DriveHistoryFolderId { get; set; }

    public string? LatestFileId { get; set; }

    public DateTime? LastBackupUtc { get; set; }

    public bool? LastBackupSucceeded { get; set; }

    public string? LastBackupError { get; set; }
}
