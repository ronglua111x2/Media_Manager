using System.Windows;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services.Gemini;

public sealed class GeminiLinkConfirmationService : IGeminiLinkConfirmationService
{
    private readonly ISettingsService _settingsService;

    public GeminiLinkConfirmationService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public bool TryConfirmPackLink()
    {
        var gemini = _settingsService.Current.Gemini ?? new GeminiSettings();
        if (!gemini.Enabled || string.IsNullOrWhiteSpace(gemini.ApiKey))
        {
            return true;
        }

        if (!gemini.ConfirmBeforeLink)
        {
            return true;
        }

        var result = AppMessageBox.Show(
            "Gemini AI is enabled. Linking will send special/OVA file names and TMDB episode data to Google for mapping. Continue?",
            "AI-assisted pack linking",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.No);

        return result == System.Windows.MessageBoxResult.Yes;
    }
}
