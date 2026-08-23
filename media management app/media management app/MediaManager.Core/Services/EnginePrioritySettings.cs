namespace media_management_app.Services;

public enum EnginePriorityMode
{
    Flat = 0,
    Ranked = 1
}

public sealed class EnginePrioritySettings
{
    public static EnginePrioritySettings Empty { get; } = new(EnginePriorityMode.Flat, [], []);

    public EnginePrioritySettings(
        EnginePriorityMode mode,
        IReadOnlyList<string> names,
        IReadOnlyList<IReadOnlyList<string>> rankGroups)
    {
        Mode = mode;
        Names = names;
        RankGroups = rankGroups;
    }

    public EnginePriorityMode Mode { get; }

    public IReadOnlyList<string> Names { get; }

    public IReadOnlyList<IReadOnlyList<string>> RankGroups { get; }
}
