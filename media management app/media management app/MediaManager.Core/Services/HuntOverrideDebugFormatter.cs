using media_management_app.Models;

namespace media_management_app.Services;

public static class HuntOverrideDebugFormatter
{
    public static string FormatOverridesFlag(bool hasOverrides) => hasOverrides ? "on" : "off";

    public static string FormatRejectOverrideSuffix(bool rejectedByOverride) =>
        rejectedByOverride ? " by=override" : string.Empty;

    public static string? FormatHuntOverrideSummary(
        int matchedWithoutOverride,
        int kept,
        IReadOnlyDictionary<CandidateRejectReason, int> overrideRejects)
    {
        if (overrideRejects.Count == 0)
        {
            return null;
        }

        var rejects = HuntLogFormatter.FormatRejectSummary(overrideRejects);
        return $"HuntOverride RecipeMatchedWithoutOverride={matchedWithoutOverride} Kept={kept} Rejects={rejects}";
    }
}
