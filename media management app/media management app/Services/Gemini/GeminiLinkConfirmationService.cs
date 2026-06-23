using System.Windows;
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

        var result = System.Windows.MessageBox.Show(
            "Gemini AI is enabled. Linking will send special/OVA file names and TMDB episode data to Google for mapping. Continue?",
            "AI-assisted pack linking",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        return result == MessageBoxResult.Yes;
    }
}
