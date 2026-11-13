using media_management_app.Common;

namespace media_management_app.Models;

public sealed class GeminiSettings
{
    public bool Enabled { get; set; }

    public string? ApiKey { get; set; }

    /// <summary>
    /// True (default): the router picks the model from the built-in catalog by rank and remaining free quota.
    /// False: use <see cref="Model"/> then <see cref="FallbackModels"/> in that order.
    /// </summary>
    public bool AutoSelectModel { get; set; } = true;

    public string Model { get; set; } = AppConstants.DefaultGeminiModel;

    public string[] FallbackModels { get; set; } = [];

    public int TimeoutSeconds { get; set; } = AppConstants.GeminiDefaultTimeoutSeconds;

    public bool ConfirmBeforeLink { get; set; } = true;
}
