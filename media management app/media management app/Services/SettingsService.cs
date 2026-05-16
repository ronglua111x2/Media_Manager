using System.Text.Json;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SettingsService()
    {
        Current = new AppSettings();
    }

    public AppSettings Current { get; private set; }

    public string SettingsFilePath => Path.Combine(Current.StateFolder, "settings.json");

    public void Load()
    {
        Directory.CreateDirectory(Current.StateFolder);

        if (!File.Exists(SettingsFilePath))
        {
            Save();
            return;
        }

        var json = File.ReadAllText(SettingsFilePath);
        var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
        Current = loaded ?? new AppSettings();
        EnsureDefaults();
    }

    public void Save()
    {
        EnsureDefaults();
        Directory.CreateDirectory(Current.StateFolder);
        var json = JsonSerializer.Serialize(Current, JsonOptions);
        File.WriteAllText(SettingsFilePath, json);
    }

    private void EnsureDefaults()
    {
        if (string.IsNullOrWhiteSpace(Current.StateFolder))
        {
            Current.StateFolder = AppConstants.DefaultStateFolder;
        }

        if (string.IsNullOrWhiteSpace(Current.OutputLibraryFolder))
        {
            Current.OutputLibraryFolder = Path.Combine(Current.StateFolder, AppConstants.DefaultLibraryFolderName);
        }
    }
}
