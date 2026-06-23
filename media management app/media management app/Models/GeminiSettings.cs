using media_management_app.Common;

namespace media_management_app.Models;

public sealed class GeminiSettings
{
    public bool Enabled { get; set; }

    public string? ApiKey { get; set; }

    public string Model { get; set; } = AppConstants.DefaultGeminiModel;

    public string[] FallbackModels { get; set; } = [];

    public int TimeoutSeconds { get; set; } = 45;

    public bool ConfirmBeforeLink { get; set; } = true;
}
