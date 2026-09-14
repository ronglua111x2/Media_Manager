using media_management_app.Models;

namespace media_management_app.Services;

public interface ISettingsService
{
    AppSettings Current { get; }

    string SettingsFilePath { get; }

    string? ActiveSettingsLogFilePath { get; }

    /// <summary>
    /// True when this <see cref="Load"/> created a new settings.json (empty first-run).
    /// </summary>
    bool CreatedNewSettingsThisLoad { get; }

    /// <summary>
    /// How this process resolved the state folder: cli-pin, pointer, legacy, or default.
    /// </summary>
    string BootstrapSource { get; }

    /// <summary>
    /// Loads settings.json from the resolved state folder, or from
    /// <paramref name="bootstrapStateFolder"/> when that path is set (CLI --state-folder).
    /// A bootstrap path is pinned for the process: copied settings.json cannot redirect
    /// StateFolder, and the LocalAppData pointer file is not read or written.
    /// </summary>
    void Load(string? bootstrapStateFolder = null);

    void Save();
}
