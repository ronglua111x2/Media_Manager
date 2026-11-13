namespace media_management_app.Models;

public sealed class GeminiModelEntry
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool Deprecated { get; set; }

    public string? Replacement { get; set; }

    public string? Notes { get; set; }

    public bool SupportsTextMapping { get; set; } = true;

    /// <summary>
    /// True when the automatic model router may use this model. Only models checked against the special-mapping
    /// cases are on; models found through ListModels start off.
    /// </summary>
    public bool AutoRoute { get; set; }

    /// <summary>Try order for the automatic router: lower first. Ties go to the newer model (listed first).</summary>
    public int Rank { get; set; }

    /// <summary>Free-tier requests per minute (from the AI Studio Rate Limit page). Null when unknown.</summary>
    public int? FreeRequestsPerMinute { get; set; }

    /// <summary>
    /// Free-tier requests per day. The router replaces this with the limit Google reports in a 429
    /// (<c>quotaValue</c>) once it has seen one.
    /// </summary>
    public int? FreeRequestsPerDay { get; set; }

    /// <summary>Gemini 3 and later: <c>thinkingConfig.thinkingLevel</c> to send (minimal, low, medium, high).</summary>
    public string? ThinkingLevel { get; set; }

    /// <summary>Gemini 2.5: <c>thinkingConfig.thinkingBudget</c> to send. Null sends no thinking config.</summary>
    public int? ThinkingBudget { get; set; }

    /// <summary>Earliest shutdown or redirect date Google announced. The router stops using the model from this date.</summary>
    public DateTime? RetireAfterUtc { get; set; }

    public string GetDisplayLabel() =>
        Deprecated ? $"{DisplayName} (deprecated)" : DisplayName;
}
