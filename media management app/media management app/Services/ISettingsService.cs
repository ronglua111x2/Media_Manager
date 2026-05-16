using media_management_app.Models;

namespace media_management_app.Services;

public interface ISettingsService
{
    AppSettings Current { get; }

    string SettingsFilePath { get; }

    void Load();

    void Save();
}
