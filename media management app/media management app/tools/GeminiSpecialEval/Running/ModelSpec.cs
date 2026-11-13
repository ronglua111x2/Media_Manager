namespace GeminiSpecialEval.Running;

/// <summary>
/// A model as the eval names it: the catalog id, optionally with a thinking level that replaces the catalog's
/// (<c>gemini-3.5-flash-lite@medium</c>). The label is what records and reports show; quotas follow the id.
/// </summary>
internal sealed record ModelSpec(string Id, string? ThinkingLevel)
{
    private static readonly string[] Levels = ["minimal", "low", "medium", "high"];

    public string Label => ThinkingLevel is null ? Id : $"{Id}@{ThinkingLevel}";

    public static ModelSpec Parse(string label)
    {
        var parts = label.Trim().Split('@', 2);
        if (parts.Length == 1 || string.IsNullOrWhiteSpace(parts[1]))
        {
            return new ModelSpec(parts[0], null);
        }

        var level = parts[1].Trim().ToLowerInvariant();
        if (!Levels.Contains(level))
        {
            throw new ArgumentException($"Unknown thinking level '{parts[1]}' in '{label}'. Use one of: {string.Join(", ", Levels)}.");
        }

        return new ModelSpec(parts[0].Trim(), level);
    }

    public static string IdOf(string label) => Parse(label).Id;
}
