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
            "Gemini AI is enabled. For special/OVA files the rules cannot place, linking sends their file names (with the name of their folder only) and the TMDB Season 0 titles and dates to Google. On the free tier Google may use this data to improve its products. Continue?",
            "AI-assisted pack linking",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.No);

        return result == System.Windows.MessageBoxResult.Yes;
    }
}
