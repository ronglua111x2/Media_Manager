using media_management_app.Common;

namespace media_management_app.Models;

public sealed class SymlinkSettings
{
    public bool Enabled { get; set; } = true;

    public string UnifiedRoot { get; set; } = AppConstants.DefaultSymlinkUnifiedRoot;

    public bool SyncOnStartup { get; set; } = true;
}
